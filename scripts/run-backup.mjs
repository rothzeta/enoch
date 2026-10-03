import { createHash, randomUUID } from 'node:crypto'
import { createReadStream } from 'node:fs'
import * as fs from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const runIdPattern = /^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$/
const topFiles = new Set([
  'manifest.json',
  'request.json',
  'checksums.sha256',
  'events.jsonl',
  'result.json',
])
const canonicalDirectories = new Set(['plans', 'events', 'evidence', 'artifacts'])

async function existingPath(value) {
  const absolute = path.resolve(value)
  let current = path.parse(absolute).root
  for (const component of absolute.slice(current.length).split(path.sep).filter(Boolean)) {
    current = path.join(current, component)
    try {
      if ((await fs.lstat(current)).isSymbolicLink())
        throw new Error(`Symlink paths are unsupported: ${current}`)
    } catch (error) {
      if (error.code !== 'ENOENT') throw error
    }
  }
  return absolute
}

function contains(parent, child) {
  const relative = path.relative(parent, child)
  return (
    relative === '' ||
    (!relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative))
  )
}

function relativePath(value) {
  if (
    typeof value !== 'string' ||
    !value ||
    value.includes('\\') ||
    value.includes('\0') ||
    path.posix.isAbsolute(value) ||
    value.split('/').some((part) => !part || part === '.' || part === '..')
  ) {
    throw new Error('Unsafe backup file path')
  }
  return value
}

async function fileHash(file) {
  const hash = createHash('sha256')
  for await (const chunk of createReadStream(file)) hash.update(chunk)
  return hash.digest('hex')
}

async function walk(directory, prefix = '') {
  const files = []
  for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
    const relative = prefix ? `${prefix}/${entry.name}` : entry.name
    const full = path.join(directory, entry.name)
    if (entry.isSymbolicLink()) throw new Error(`Symlink content is unsupported: ${relative}`)
    if (entry.isDirectory()) files.push(...(await walk(full, relative)))
    else if (entry.isFile()) files.push(relativePath(relative))
    else throw new Error(`Unsupported storage entry: ${relative}`)
  }
  return files.sort()
}

async function readJson(file) {
  return JSON.parse(await fs.readFile(file, 'utf8'))
}

async function validateRun(dataRoot, id) {
  if (!runIdPattern.test(id)) throw new Error(`Invalid stored run id: ${id}`)
  const directory = path.join(dataRoot, 'runs', id)
  for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
    if (entry.isDirectory() ? !canonicalDirectories.has(entry.name) : !topFiles.has(entry.name)) {
      throw new Error(
        `Run ${id} has pending or unrecognized data (${entry.name}); recover it before backup`,
      )
    }
  }
  const files = await walk(directory)
  for (const name of ['manifest.json', 'request.json', 'checksums.sha256']) {
    if (!files.includes(name)) throw new Error(`Incomplete run ${id}: missing ${name}`)
  }
  if ((await readJson(path.join(directory, 'manifest.json')))?.id !== id)
    throw new Error(`Invalid manifest for ${id}`)
  for (const file of files) {
    if (file.endsWith('.tmp') || file.split('/').some((component) => component.startsWith('.')))
      throw new Error(`Uncommitted file in ${id}: ${file}`)
    if (file.endsWith('.json')) {
      const value = await readJson(path.join(directory, file))
      if (
        value === null &&
        (file === 'request.json' || file === 'result.json' || file.startsWith('plans/'))
      )
        throw new Error(`Unreadable null publication in ${id}: ${file}`)
    }
  }
  const checksums = new Map()
  for (const line of (await fs.readFile(path.join(directory, 'checksums.sha256'), 'utf8'))
    .split('\n')
    .filter(Boolean)) {
    const match = /^([a-f0-9]{64})  (.+)$/.exec(line.replace(/\r$/, ''))
    if (!match) throw new Error(`Invalid checksums in ${id}`)
    const name = relativePath(match[2])
    if (checksums.has(name)) throw new Error(`Duplicate checksum path in ${id}`)
    checksums.set(name, match[1])
  }
  const expectedFiles = files.filter((file) => file !== 'checksums.sha256')
  if (JSON.stringify([...checksums.keys()].sort()) !== JSON.stringify(expectedFiles))
    throw new Error(`Checksum membership mismatch in ${id}`)
  for (const file of expectedFiles) {
    if ((await fileHash(path.join(directory, file))) !== checksums.get(file))
      throw new Error(`Checksum mismatch in ${id}: ${file}`)
  }
  return files.map((file) => `runs/${id}/${file}`)
}

async function storageFiles(dataRoot, backup = false) {
  const permitted = new Set(
    backup ? ['runs', 'backup-manifest.json'] : ['runs', '.enoch-store.lock', '.staging'],
  )
  for (const entry of await fs.readdir(dataRoot, { withFileTypes: true })) {
    if (entry.isSymbolicLink() || !permitted.has(entry.name))
      throw new Error(`Unrecognized data-root entry: ${entry.name}`)
    if (
      entry.name === '.staging' &&
      (!entry.isDirectory() || (await fs.readdir(path.join(dataRoot, entry.name))).length)
    )
      throw new Error('Unresolved creation stages; recover and quiesce before backup')
    if (entry.name === '.enoch-store.lock' && !entry.isFile())
      throw new Error('Invalid store ownership marker')
  }
  const files = []
  for (const entry of await fs.readdir(path.join(dataRoot, 'runs'), { withFileTypes: true })) {
    if (!backup && entry.name === '.enoch-store.lock' && entry.isFile()) continue
    if (
      !backup &&
      entry.name === '.staging' &&
      entry.isDirectory() &&
      !(await fs.readdir(path.join(dataRoot, 'runs', entry.name))).length
    )
      continue
    if (entry.name === '.staging')
      throw new Error('Unresolved creation stages; recover and quiesce before backup')
    if (!entry.isDirectory() || entry.isSymbolicLink())
      throw new Error('Run storage contains an unexpected entry')
    files.push(...(await validateRun(dataRoot, entry.name)))
  }
  return files.sort()
}

async function describeFiles(root, files) {
  const descriptions = []
  for (const file of files)
    descriptions.push({
      path: file,
      length: (await fs.stat(path.join(root, file))).size,
      sha256: await fileHash(path.join(root, file)),
    })
  return descriptions
}

async function verifyBackup(directory) {
  const manifest = await readJson(path.join(directory, 'backup-manifest.json'))
  if (manifest?.version !== 1 || !Array.isArray(manifest.files))
    throw new Error('Unsupported backup manifest')
  const names = new Set()
  for (const file of manifest.files) {
    relativePath(file?.path)
    if (
      !file.path.startsWith('runs/') ||
      names.has(file.path) ||
      !Number.isSafeInteger(file.length) ||
      file.length < 0 ||
      !/^[a-f0-9]{64}$/.test(file.sha256)
    )
      throw new Error('Invalid backup manifest entry')
    names.add(file.path)
  }
  const actualFiles = await storageFiles(directory, true)
  if (JSON.stringify([...names].sort()) !== JSON.stringify(actualFiles))
    throw new Error('Backup file membership mismatch')
  const actual = await describeFiles(directory, actualFiles)
  for (const file of actual) {
    const expected = manifest.files.find((item) => item.path === file.path)
    if (file.length !== expected.length || file.sha256 !== expected.sha256)
      throw new Error(`Backup checksum mismatch: ${file.path}`)
  }
  return actualFiles
}

async function publishCopy(source, target, files, afterCopy) {
  try {
    await fs.lstat(target)
    throw new Error('Destination already exists; choose a new empty path')
  } catch (error) {
    if (error.code !== 'ENOENT') throw error
  }
  await fs.mkdir(path.dirname(target), { recursive: true })
  const stage = `${target}.enoch-copy-${randomUUID()}`
  await fs.mkdir(stage)
  try {
    await fs.mkdir(path.join(stage, 'runs'))
    for (const file of files) {
      const destination = path.join(stage, file)
      await fs.mkdir(path.dirname(destination), { recursive: true })
      await fs.copyFile(path.join(source, file), destination)
    }
    await afterCopy(stage)
    // The parent and destination were validated; publish only a complete verified copy.
    try {
      await fs.lstat(target)
      throw new Error('Destination was created during copying; choose a new path')
    } catch (error) {
      if (error.code !== 'ENOENT') throw error
    }
    await fs.rename(stage, target)
  } catch (error) {
    await fs.rm(stage, { recursive: true, force: true })
    throw error
  }
}

export async function backup(sourceValue, targetValue, quiesced) {
  if (quiesced !== '--quiesced')
    throw new Error(
      'Stop every writer first, then supply --quiesced; copying cannot establish writer quiescence',
    )
  const source = await existingPath(sourceValue)
  const target = await existingPath(targetValue)
  if (contains(source, target) || contains(target, source))
    throw new Error('Source and destination must not overlap')
  const files = await storageFiles(source)
  const manifest = { version: 1, files: await describeFiles(source, files) }
  await publishCopy(source, target, files, async (stage) => {
    await fs.writeFile(
      path.join(stage, 'backup-manifest.json'),
      JSON.stringify(manifest, null, 2) + '\n',
    )
    await verifyBackup(stage)
    // A changed source fails verification; this is detection, not live-copy consistency.
    if (
      JSON.stringify(await describeFiles(source, await storageFiles(source))) !==
      JSON.stringify(manifest.files)
    )
      throw new Error('Source changed during backup; stop all writers and retry')
  })
  return manifest.files.length
}

export async function restore(sourceValue, targetValue) {
  const source = await existingPath(sourceValue)
  const target = await existingPath(targetValue)
  if (contains(source, target) || contains(target, source))
    throw new Error('Source and destination must not overlap')
  const files = await verifyBackup(source)
  await publishCopy(source, target, files, async (stage) => {
    await fs.copyFile(
      path.join(source, 'backup-manifest.json'),
      path.join(stage, 'backup-manifest.json'),
    )
    await verifyBackup(stage)
    await fs.unlink(path.join(stage, 'backup-manifest.json'))
  })
  return files.length
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  try {
    const [operation, source, target, confirmation] = process.argv.slice(2)
    if (!source || !target || !['backup', 'restore'].includes(operation))
      throw new Error('Usage: backup|restore source destination [--quiesced]')
    const count =
      operation === 'backup'
        ? await backup(source, target, confirmation)
        : await restore(source, target)
    console.log(`${operation}: verified ${count} canonical files`)
  } catch (error) {
    console.error(error.message)
    process.exitCode = 1
  }
}
