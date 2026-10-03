import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from './api'

afterEach(() => vi.unstubAllGlobals())

describe('API transport', () => {
  it('requests encoded run IDs and adapts the real RunDocument envelope', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          manifest: {
            id: 'run /?#',
            title: 'Published',
            state: 'finished',
            outcome: 'failed',
            summary: 'Stopped',
          },
          request: 'task',
          plans: [],
          events: [],
          evidence: [],
          artifacts: [],
          result: null,
        }),
      ),
    )
    vi.stubGlobal('fetch', fetchMock)
    const run = await api.getRun('run /?#')
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/v1/runs/run%20%2F%3F%23')
    expect(run.id).toBe('run /?#')
    expect(run.outcome).toBe('failed')
    expect(run.summary).toBe('Stopped')
    expect(run.request).toBe('task')
    expect(run.result).toBeNull()
  })

  it('reports HTTP failures instead of returning a run', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 503 })))
    await expect(api.getRun('run')).rejects.toThrow('503')
  })

  it('rejects an invalid detail envelope', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"manifest":null}')))
    await expect(api.getRun('run')).rejects.toThrow('Invalid run response')
  })
})
