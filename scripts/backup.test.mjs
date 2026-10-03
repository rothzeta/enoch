import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import * as fs from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import test from 'node:test'
import { backup, restore } from './run-backup.mjs'

async function fixture(t) {
  const base = await fs.mkdtemp(path.join(os.tmpdir(), 'enoch-backup-test-'))
  t.after(() => fs.rm(base, { recursive: true, force: true }))
  const source = path.join(base, 'source')
  const run = path.join(source, 'runs', 'run1')
  await fs.mkdir(path.join(run, 'plans'), { recursive: true })
  const files = {
    'manifest.json': JSON.stringify({
      id: 'run1',
      title: 'Fixture',
      state: 'running',
      outcome: null,
      sequence: 2,
      createdAt: '2026-10-03T00:00:00Z',
      updatedAt: '2026-10-03T00:00:00Z',
      finishedAt: null,
      summary: null,
    }),
    'request.json': '{"input":"fixture"}',
    'plans/0001.json': '{"steps":["work"]}',
    'events.jsonl':
      '{"sequence":2,"eventId":"event1","kind":"progress","data":null,"occurredAt":"2026-10-03T00:00:00Z"}\n',
    'result.json': '"answer"',
  }
  for (const [name, value] of Object.entries(files)) await fs.writeFile(path.join(run, name), value)
  await fs.writeFile(
    path.join(run, 'checksums.sha256'),
    Object.entries(files)
      .map(([name, value]) => `${createHash('sha256').update(value).digest('hex')}  ${name}`)
      .sort()
      .join('\n') + '\n',
  )
  return {
    base,
    source,
    run,
    saved: path.join(base, 'saved'),
    restored: path.join(base, 'restored'),
  }
}

async function absent(file) {
  await assert.rejects(fs.stat(file), { code: 'ENOENT' })
}

test('backup requires explicit quiescence and roundtrips every canonical file', async (t) => {
  const f = await fixture(t)
  await assert.rejects(backup(f.source, f.saved), /Stop every writer/)
  await absent(f.saved)
  assert.equal(await backup(f.source, f.saved, '--quiesced'), 6)
  assert.equal(await restore(f.saved, f.restored), 6)
  const original = await fs.readFile(path.join(f.run, 'plans/0001.json'))
  assert.deepEqual(await fs.readFile(path.join(f.restored, 'runs/run1/plans/0001.json')), original)
  await absent(path.join(f.restored, 'backup-manifest.json'))
})

test('existing destinations are never replaced even when empty', async (t) => {
  const f = await fixture(t)
  await backup(f.source, f.saved, '--quiesced')
  await fs.mkdir(f.restored)
  await assert.rejects(restore(f.saved, f.restored), /Destination already exists/)
  assert.deepEqual(await fs.readdir(f.restored), [])
  await assert.rejects(backup(f.source, f.saved, '--quiesced'), /Destination already exists/)
})

test('corrupt copied content cannot publish a restore', async (t) => {
  const f = await fixture(t)
  await backup(f.source, f.saved, '--quiesced')
  await fs.writeFile(path.join(f.saved, 'runs/run1/request.json'), '"changed"')
  await assert.rejects(restore(f.saved, f.restored), /Checksum mismatch/)
  await absent(f.restored)
})

test('missing and extra canonical files cannot publish a restore', async (t) => {
  const f = await fixture(t)
  await backup(f.source, f.saved, '--quiesced')
  const request = path.join(f.saved, 'runs/run1/request.json')
  const body = await fs.readFile(request)
  await fs.unlink(request)
  await assert.rejects(restore(f.saved, f.restored), /Incomplete run/)
  await fs.writeFile(request, body)
  await fs.writeFile(path.join(f.saved, 'runs/run1/plans/0002.json'), 'true')
  await assert.rejects(restore(f.saved, f.restored), /membership mismatch/)
  await absent(f.restored)
})

test('unsafe backup manifest paths are rejected without external writes', async (t) => {
  const f = await fixture(t)
  await backup(f.source, f.saved, '--quiesced')
  const manifestFile = path.join(f.saved, 'backup-manifest.json')
  const manifest = JSON.parse(await fs.readFile(manifestFile, 'utf8'))
  manifest.files[0].path = '../escaped'
  await fs.writeFile(manifestFile, JSON.stringify(manifest))
  await assert.rejects(restore(f.saved, f.restored), /Unsafe backup file path/)
  await absent(f.restored)
  await absent(path.join(f.base, 'escaped'))
})

test('source overlap and symlink ancestors are rejected', async (t) => {
  const f = await fixture(t)
  await assert.rejects(
    backup(f.source, path.join(f.source, 'nested'), '--quiesced'),
    /must not overlap/,
  )
  const link = path.join(f.base, 'link')
  await fs.symlink(f.source, link, 'dir')
  await assert.rejects(backup(link, f.saved, '--quiesced'), /Symlink paths/)
  await assert.rejects(backup(f.source, path.join(link, 'nested'), '--quiesced'), /Symlink paths/)
})

test('pending journals and abandoned creation stages require recovery before backup', async (t) => {
  const f = await fixture(t)
  const pending = path.join(f.run, '.publication-pending')
  await fs.mkdir(pending)
  await assert.rejects(backup(f.source, f.saved, '--quiesced'), /pending or unrecognized/)
  await fs.rmdir(pending)
  await fs.mkdir(path.join(f.source, 'runs/.staging/create-012345678901234567890123456789ab'), {
    recursive: true,
  })
  await assert.rejects(backup(f.source, f.saved, '--quiesced'), /Unresolved creation stages/)
  await absent(f.saved)
})

test('incomplete/null published data is rejected rather than omitted', async (t) => {
  const f = await fixture(t)
  await fs.writeFile(path.join(f.run, 'request.json'), 'null')
  await assert.rejects(backup(f.source, f.saved, '--quiesced'), /Unreadable null publication/)
  await absent(f.saved)
})

test('symlink file content is rejected', async (t) => {
  const f = await fixture(t)
  await fs.unlink(path.join(f.run, 'request.json'))
  await fs.symlink(path.join(f.run, 'result.json'), path.join(f.run, 'request.json'))
  await assert.rejects(backup(f.source, f.saved, '--quiesced'), /Symlink content/)
  await absent(f.saved)
})

test('mounted-runs metadata is excluded and cannot be smuggled into a backup', async (t) => {
  const f = await fixture(t)
  await fs.writeFile(path.join(f.source, 'runs/.enoch-store.lock'), '')
  await fs.mkdir(path.join(f.source, 'runs/.staging'))
  await backup(f.source, f.saved, '--quiesced')
  await absent(path.join(f.saved, 'runs/.enoch-store.lock'))
  await absent(path.join(f.saved, 'runs/.staging'))
  await fs.writeFile(path.join(f.saved, 'unexpected'), 'ignored?')
  await assert.rejects(restore(f.saved, f.restored), /Unrecognized data-root entry/)
  await fs.unlink(path.join(f.saved, 'unexpected'))
  await fs.writeFile(path.join(f.saved, 'runs/.enoch-store.lock'), 'unlisted')
  await assert.rejects(restore(f.saved, f.restored), /unexpected entry/)
  await absent(f.restored)
})

test('additive event segments and legacy event logs roundtrip together', async (t) => {
  const f = await fixture(t)
  await fs.mkdir(path.join(f.run, 'events'))
  const body = JSON.stringify({
    sequence: 3,
    eventId: 'event2',
    kind: 'progress',
    occurredAt: '2026-10-03T00:00:00Z',
  })
  await fs.writeFile(path.join(f.run, 'events/0003.json'), body)
  const manifestPath = path.join(f.run, 'manifest.json')
  const manifest = JSON.parse(await fs.readFile(manifestPath, 'utf8'))
  manifest.sequence = 3
  const manifestBody = JSON.stringify(manifest)
  await fs.writeFile(manifestPath, manifestBody)
  const checksumPath = path.join(f.run, 'checksums.sha256')
  const checksums = (await fs.readFile(checksumPath, 'utf8'))
    .split('\n')
    .filter((line) => line && !line.endsWith('  manifest.json'))
  checksums.push(`${createHash('sha256').update(manifestBody).digest('hex')}  manifest.json`)
  checksums.push(`${createHash('sha256').update(body).digest('hex')}  events/0003.json`)
  await fs.writeFile(checksumPath, checksums.sort().join('\n') + '\n')
  assert.equal(await backup(f.source, f.saved, '--quiesced'), 7)
  assert.equal(await restore(f.saved, f.restored), 7)
  assert.equal(await fs.readFile(path.join(f.restored, 'runs/run1/events/0003.json'), 'utf8'), body)
  assert.equal(
    await fs.readFile(path.join(f.restored, 'runs/run1/events.jsonl'), 'utf8'),
    await fs.readFile(path.join(f.run, 'events.jsonl'), 'utf8'),
  )
})
