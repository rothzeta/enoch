import assert from 'node:assert/strict'
import * as fs from 'node:fs/promises'
import { randomBytes } from 'node:crypto'
import { createHash } from 'node:crypto'

const [mode, address, fixtureFile] = process.argv.slice(2)
const token = process.env.ENOCH_TOKEN
const auth = { Authorization: `Bearer ${token}` }

async function request(route, options = {}) {
  const response = await fetch(`${address}${route}`, {
    ...options,
    signal: AbortSignal.timeout(10000),
  })
  return { response, text: await response.text() }
}

async function json(route, value, method = 'POST', headers = auth) {
  return request(route, {
    method,
    headers: { 'Content-Type': 'application/json', ...headers },
    body: JSON.stringify(value),
  })
}

async function waitForApi() {
  const deadline = Date.now() + 45000
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${address}/health`, { signal: AbortSignal.timeout(1000) })
      if (response.ok) return
    } catch {
      /* The task-owned API may still be starting. */
    }
    await new Promise((resolve) => setTimeout(resolve, 250))
  }
  throw new Error('Local API did not become healthy within 45 seconds')
}

async function successful(route, body, method = 'POST') {
  const result = await json(route, body, method)
  assert.ok(result.response.ok, `${route}: HTTP ${result.response.status}`)
  return JSON.parse(result.text)
}

async function verifyArtifactStream(runId, artifactId, expectedLength, expectedHash) {
  const response = await fetch(`${address}/api/v1/runs/${runId}/artifacts/${artifactId}`, {
    signal: AbortSignal.timeout(45000),
  })
  assert.equal(response.status, 200)
  assert.equal(Number(response.headers.get('content-length')), expectedLength)
  const hash = createHash('sha256')
  let length = 0
  for await (const chunk of response.body) {
    length += chunk.length
    hash.update(chunk)
  }
  assert.equal(length, expectedLength)
  assert.equal(hash.digest('hex'), expectedHash)
}

await waitForApi()
if (mode === 'seed') {
  const forbidden = { title: 'Unauthorized', request: {}, runId: 'forbidden' }
  assert.equal((await json('/api/v1/publish/runs', forbidden, 'POST', {})).response.status, 401)
  assert.equal(
    (await json('/api/v1/publish/runs', forbidden, 'POST', { Authorization: 'Bearer wrong' }))
      .response.status,
    401,
  )
  assert.equal((await request('/api/v1/runs/forbidden')).response.status, 404)
  await successful('/api/v1/publish/runs', {
    title: 'Local restore fixture',
    request: { prompt: 'Recover every publication' },
    runId: 'operational-finished',
  })
  const base = '/api/v1/publish/runs/operational-finished'
  await successful(`${base}/plans`, { plan: { revision: 1 }, eventId: 'plan1' })
  await successful(`${base}/plans`, { plan: { revision: 2 }, eventId: 'plan2' })
  await successful(`${base}/events`, {
    eventId: 'progress1',
    kind: 'progress',
    data: { percent: 20 },
  })
  await successful(`${base}/events`, {
    eventId: 'progress2',
    kind: 'progress',
    data: { percent: 100 },
  })
  const evidence = await successful(`${base}/evidence`, {
    name: 'notes.txt',
    content: 'Restored evidence body\n',
    mimeType: 'text/plain',
  })
  const artifactBytes = randomBytes(257)
  const artifactResult = await request(`${base}/artifacts`, {
    method: 'POST',
    headers: {
      ...auth,
      'X-Artifact-Name': 'fixture.bin',
      'Content-Type': 'application/octet-stream',
    },
    body: artifactBytes,
  })
  assert.ok(artifactResult.response.ok)
  const artifact = JSON.parse(artifactResult.text)
  await successful(`${base}/result`, { result: { answer: 'Restored' } }, 'PUT')
  await successful(`${base}/finish`, {
    outcome: 'partial',
    summary: 'Local complete-bundle recovery verified',
  })
  await successful('/api/v1/publish/runs', {
    title: 'Unaffected active fixture',
    request: 42,
    runId: 'operational-active',
  })
  const bundle = JSON.parse((await request('/api/v1/runs/operational-finished/bundle')).text)
  const active = JSON.parse((await request('/api/v1/runs/operational-active/bundle')).text)
  const fixture = {
    bundle,
    active,
    evidenceId: evidence.id,
    evidenceBody: 'Restored evidence body\n',
    artifactId: artifact.id,
    artifactHash: createHash('sha256').update(artifactBytes).digest('hex'),
  }
  await fs.writeFile(fixtureFile, JSON.stringify(fixture))
  console.log(
    'Local bearer gate rejects missing/wrong credentials; valid publication and anonymous reads succeeded',
  )
} else if (mode === 'burst') {
  await successful('/api/v1/publish/runs', {
    title: 'Killed publication burst',
    request: 'Process interruption drill',
    runId: 'operational-killed',
  })
  const authored = Array.from({ length: 64 }, (_, index) => ({
    route: `/api/v1/publish/runs/operational-killed/${index % 2 === 0 ? 'plans' : 'events'}`,
    body:
      index % 2 === 0
        ? { plan: { index, content: 'p'.repeat(256 * 1024) }, eventId: `kill-plan-${index}` }
        : {
            eventId: `kill-event-${index}`,
            kind: 'progress',
            data: { index, content: 'e'.repeat(256 * 1024) },
          },
  }))
  const acknowledged = []
  await fs.writeFile(`${fixtureFile}.burst`, JSON.stringify({ authored, acknowledged }))
  for (const [index, item] of authored.entries()) {
    try {
      const pending = successful(item.route, item.body).then(
        (response) => ({ response }),
        (error) => ({ error }),
      )
      if (index === 1)
        await fs.writeFile(
          `${fixtureFile}.kill-ready`,
          'An acknowledged plan exists and a progress publication has started.\n',
        )
      const outcome = await pending
      if (outcome.error) throw outcome.error
      const response = outcome.response
      acknowledged.push(response)
      await fs.writeFile(`${fixtureFile}.burst`, JSON.stringify({ authored, acknowledged }))
    } catch (error) {
      if (index === 0) throw error
      console.log(
        `Publication burst interrupted after ${acknowledged.length} acknowledged responses`,
      )
      process.exit(0)
    }
  }
  await fs.writeFile(`${fixtureFile}.burst-complete`, 'Unexpectedly completed before SIGKILL\n')
  throw new Error('Publication burst completed before the process was killed')
} else if (mode === 'recover-burst') {
  const { authored, acknowledged } = JSON.parse(await fs.readFile(`${fixtureFile}.burst`, 'utf8'))
  assert.ok(acknowledged.length > 0 && acknowledged.length < authored.length)
  const responses = []
  for (const [index, item] of authored.entries()) {
    const response = await successful(item.route, item.body)
    if (index < acknowledged.length) assert.deepEqual(response, acknowledged[index])
    responses.push(response)
  }
  assert.deepEqual(
    responses.map((response) => response.sequence),
    Array.from({ length: authored.length }, (_, index) => index + 1),
  )
  const bundle = JSON.parse((await request('/api/v1/runs/operational-killed/bundle')).text)
  assert.equal(bundle.manifest.sequence, authored.length)
  assert.equal(bundle.plans.length, authored.length / 2)
  assert.equal(bundle.events.length, authored.length / 2)
  for (const [index, plan] of bundle.plans.entries())
    assert.deepEqual(plan, authored[index * 2].body.plan)
  for (const [index, event] of bundle.events.entries()) {
    assert.equal(event.sequence, index * 2 + 2)
    assert.equal(event.eventId, authored[index * 2 + 1].body.eventId)
    assert.deepEqual(event.data, authored[index * 2 + 1].body.data)
  }
  const fixture = JSON.parse(await fs.readFile(fixtureFile, 'utf8'))
  fixture.killed = bundle
  await fs.writeFile(fixtureFile, JSON.stringify(fixture))
  console.log(
    'Real SIGKILL/restart preserved acknowledged identities; retried 64 plans/events with consecutive unique sequences and readable content',
  )
} else if (mode === 'bounded-upload') {
  const runId = 'operational-upload'
  await successful('/api/v1/publish/runs', {
    title: 'Real chunked upload boundary',
    request: '64 MiB streamed artifact contract',
    runId,
  })
  const limit = 64 * 1024 * 1024
  const block = Buffer.alloc(64 * 1024, 0xa5)
  const expected = createHash('sha256')
  for (let offset = 0; offset < limit; offset += block.length) expected.update(block)
  const expectedHash = expected.digest('hex')
  async function* bytes(length) {
    for (let offset = 0; offset < length; offset += block.length) {
      yield block.subarray(0, Math.min(block.length, length - offset))
    }
  }
  async function upload(length, name) {
    return fetch(`${address}/api/v1/publish/runs/${runId}/artifacts`, {
      method: 'POST',
      headers: { ...auth, 'Content-Type': 'application/octet-stream', 'X-Artifact-Name': name },
      body: bytes(length),
      duplex: 'half',
      signal: AbortSignal.timeout(45000),
    })
  }
  const accepted = await upload(limit, 'accepted.bin')
  assert.equal(accepted.status, 200)
  const metadata = await accepted.json()
  assert.equal(metadata.length, limit)
  assert.equal(metadata.sha256, expectedHash)
  await verifyArtifactStream(runId, metadata.id, limit, expectedHash)
  const before = JSON.parse((await request(`/api/v1/runs/${runId}/bundle`)).text)
  const rejected = await upload(limit + 1, 'rejected.bin')
  assert.equal(rejected.status, 413)
  assert.equal((await rejected.json()).code, 'too_large')
  assert.deepEqual(JSON.parse((await request(`/api/v1/runs/${runId}/bundle`)).text), before)
  assert.equal(before.artifacts.length, 1)
  const fixture = JSON.parse(await fs.readFile(fixtureFile, 'utf8'))
  fixture.upload = { bundle: before, artifactId: metadata.id, length: limit, hash: expectedHash }
  await fs.writeFile(fixtureFile, JSON.stringify(fixture))
  console.log(
    'Real Kestrel chunked exact64MiB accepted with exact streamed hash;64MiB+1 rejected413 without publication',
  )
} else if (mode === 'verify') {
  const fixture = JSON.parse(await fs.readFile(fixtureFile, 'utf8'))
  const bundle = await request('/api/v1/runs/operational-finished/bundle')
  assert.equal(bundle.response.status, 200)
  assert.deepEqual(JSON.parse(bundle.text), fixture.bundle)
  assert.deepEqual(
    JSON.parse((await request('/api/v1/runs/operational-active/bundle')).text),
    fixture.active,
  )
  assert.deepEqual(
    JSON.parse((await request('/api/v1/runs/operational-killed/bundle')).text),
    fixture.killed,
  )
  assert.deepEqual(
    JSON.parse((await request('/api/v1/runs/operational-upload/bundle')).text),
    fixture.upload.bundle,
  )
  await verifyArtifactStream(
    'operational-upload',
    fixture.upload.artifactId,
    fixture.upload.length,
    fixture.upload.hash,
  )
  const evidence = await request(`/api/v1/runs/operational-finished/evidence/${fixture.evidenceId}`)
  assert.equal(evidence.response.status, 200)
  assert.equal(evidence.text, fixture.evidenceBody)
  const artifact = await fetch(
    `${address}/api/v1/runs/operational-finished/artifacts/${fixture.artifactId}`,
    { signal: AbortSignal.timeout(10000) },
  )
  assert.equal(artifact.status, 200)
  assert.equal(
    createHash('sha256')
      .update(Buffer.from(await artifact.arrayBuffer()))
      .digest('hex'),
    fixture.artifactHash,
  )
  const page = await request('/runs/operational-finished')
  assert.equal(page.response.status, 200)
  assert.match(page.text, /<div id="app">/)
  assert.equal(
    (
      await json(
        '/api/v1/publish/runs',
        { title: 'Forbidden', request: true, runId: 'forbidden' },
        'POST',
        {},
      )
    ).response.status,
    401,
  )
  console.log(
    'Restored bundles, evidence, artifact bytes, terminal metadata, browser entry and local bearer gate verified',
  )
} else if (mode === 'cli') {
  const before = JSON.parse(await fs.readFile(`${fixtureFile}/before-finish.json`, 'utf8'))
  const rejected = JSON.parse(await fs.readFile(`${fixtureFile}/after-rejection.json`, 'utf8'))
  const after = JSON.parse(await fs.readFile(`${fixtureFile}/after-finish.json`, 'utf8'))
  assert.equal(before.manifest.state, 'running')
  assert.equal(before.manifest.outcome, null)
  assert.deepEqual(rejected, before)
  assert.equal(before.result, await fs.readFile(`${fixtureFile}/result.txt`, 'utf8'))
  assert.equal(after.request, await fs.readFile(`${fixtureFile}/request.txt`, 'utf8'))
  assert.equal(after.plans.length, 1)
  assert.equal(after.plans[0].content, await fs.readFile(`${fixtureFile}/plan.txt`, 'utf8'))
  assert.equal(after.events.length, 1)
  assert.equal(after.events[0].eventId, 'cli-docker-progress')
  assert.equal(after.manifest.state, 'finished')
  assert.equal(after.manifest.outcome, 'failed')
  assert.equal(after.manifest.summary, 'CLI explicit finish verified')
  const evidence = await request(
    `/api/v1/runs/${after.manifest.id}/evidence/${after.evidence[0].id}`,
  )
  assert.equal(evidence.response.status, 200)
  assert.equal(evidence.text, await fs.readFile(`${fixtureFile}/evidence.txt`, 'utf8'))
  const artifact = await fetch(
    `${address}/api/v1/runs/${after.manifest.id}/artifacts/${after.artifacts[0].id}`,
    { signal: AbortSignal.timeout(10000) },
  )
  assert.equal(artifact.status, 200)
  assert.deepEqual(
    Buffer.from(await artifact.arrayBuffer()),
    await fs.readFile(`${fixtureFile}/artifact.bin`),
  )
  assert.match(
    await fs.readFile(`${fixtureFile}/rejected-error.txt`, 'utf8'),
    /Use finish --outcome/,
  )
  console.log(
    'Actual CLI start/plan/progress/evidence/artifact/result/finish/read and unchanged unsupported result verified',
  )
} else if (mode === 'owner') {
  const result = await request('/api/v1/runs')
  assert.equal(result.response.status, 409)
  assert.equal(JSON.parse(result.text).code, 'store_in_use')
  console.log('A second API cannot acquire ownership of the same mounted runs volume')
} else if (mode === 'health') {
  const page = await request('/')
  assert.equal(page.response.status, 200)
  assert.match(page.text, /<div id="app">/)
  assert.equal((await request('/api/v1/runs')).response.status, 200)
  assert.equal(
    (
      await json(
        '/api/v1/publish/runs',
        { title: 'Forbidden', request: true, runId: 'forbidden' },
        'POST',
        {},
      )
    ).response.status,
    401,
  )
  assert.equal(
    (
      await json(
        '/api/v1/publish/runs',
        { title: 'Forbidden', request: true, runId: 'forbidden' },
        'POST',
        { Authorization: 'Bearer wrong' },
      )
    ).response.status,
    401,
  )
  console.log('Local browser/API health and rejected publisher credentials verified')
} else {
  throw new Error(
    'Expected seed, burst, recover-burst, bounded-upload, verify, cli, owner or health mode',
  )
}
