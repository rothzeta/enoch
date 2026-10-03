// @vitest-environment jsdom

import { createApp, nextTick, type App as VueApp } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App.vue'
import { api, artifactDownloadUrl, evidenceDownloadUrl } from './api'
import type * as ApiModule from './api'

const { artifact } = vi.hoisted(() => ({
  artifact: {
    id: 'artifact /?#',
    name: 'curated-flac-integrity.csv',
    mimeType: 'text/csv',
    length: 1_153_087,
    sha256: 'abc123',
    createdAt: '2026-09-06T16:55:10Z',
  },
}))

vi.mock('./api', async (importOriginal) => {
  const original = await importOriginal<typeof ApiModule>()
  return {
    ...original,
    api: {
      listRuns: vi.fn().mockResolvedValue([]),
      getRun: vi.fn().mockResolvedValue({
        id: 'run /?#',
        title: 'Artifact run',
        state: 'Finished',
        request: {},
        artifacts: [artifact],
      }),
    },
  }
})

const mountedApps: VueApp[] = []

function mountApp() {
  const root = document.createElement('div')
  document.body.append(root)
  const app = createApp(App)
  mountedApps.push(app)
  app.mount(root)
  return root
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: Error) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

const detailFixture = (id: string, outcome = 'failed') => ({
  id,
  title: `Run ${id}`,
  state: 'finished',
  outcome,
  summary: `Summary ${id}`,
  request: 'task',
  plans: [],
  events: [],
  result: null,
  evidence: [],
  artifacts: [],
})

beforeEach(() => {
  vi.mocked(api.listRuns).mockResolvedValue([])
  vi.mocked(api.getRun).mockResolvedValue(detailFixture('run'))
})

afterEach(() => {
  mountedApps.splice(0).forEach((app) => app.unmount())
  document.body.innerHTML = ''
  history.replaceState({}, '', '/')
  vi.restoreAllMocks()
  vi.clearAllMocks()
  vi.useRealTimers()
})

describe('artifact downloads', () => {
  it('constructs a same-origin URL with encoded route segments', () => {
    expect(artifactDownloadUrl('run /?#', 'artifact /?#')).toBe(
      '/api/v1/runs/run%20%2F%3F%23/artifacts/artifact%20%2F%3F%23',
    )
  })

  it('renders artifact metadata and a visible download link', async () => {
    history.replaceState({}, '', '/runs/run-id')
    vi.mocked(api.getRun).mockResolvedValue({ ...detailFixture('run /?#'), artifacts: [artifact] })
    const root = mountApp()
    await vi.waitFor(() => expect(root.querySelectorAll('.tabs button')).toHaveLength(6))

    const tabButtons = [...root.querySelectorAll<HTMLButtonElement>('.tabs button')]
    const artifactsTab = tabButtons.find((button) => button.textContent === 'artifacts')
    expect(artifactsTab).toBeDefined()
    artifactsTab!.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await nextTick()

    expect(root.textContent).toContain(artifact.name)
    expect(root.textContent).toContain(artifact.mimeType)
    expect(root.textContent).toContain(String(artifact.length))
    expect(root.textContent).toContain(artifact.sha256)
    const link = root.querySelector<HTMLAnchorElement>('a[download]')
    expect(link?.textContent).toContain('Download')
    expect(link?.getAttribute('href')).toBe(artifactDownloadUrl('run /?#', artifact.id))
  })
})

describe('reader state', () => {
  it('lets a slow initial request finish across polling intervals', async () => {
    vi.useFakeTimers()
    const initial = deferred<never[]>()
    vi.mocked(api.listRuns).mockReturnValue(initial.promise)
    const root = mountApp()
    const signal = vi.mocked(api.listRuns).mock.calls[0]?.[0]
    await vi.advanceTimersByTimeAsync(21_000)
    expect(api.listRuns).toHaveBeenCalledTimes(1)
    expect(signal?.aborted).toBe(false)
    initial.resolve([])
    await vi.advanceTimersByTimeAsync(0)
    expect(root.textContent).toContain('No runs have been published')
    await vi.advanceTimersByTimeAsync(7_000)
    expect(api.listRuns).toHaveBeenCalledTimes(2)
  })

  it('provides evidence bytes through a visible download link', async () => {
    history.replaceState({}, '', '/runs/run')
    vi.mocked(api.getRun).mockResolvedValue({ ...detailFixture('run'), evidence: [artifact] })
    const root = mountApp()
    await vi.waitFor(() => expect(root.querySelectorAll('.tabs button')).toHaveLength(6))
    const evidenceTab = [...root.querySelectorAll<HTMLButtonElement>('.tabs button')].find(
      (button) => button.textContent === 'evidence',
    )!
    evidenceTab.click()
    await nextTick()
    const link = root.querySelector<HTMLAnchorElement>('a[download]')
    expect(link?.getAttribute('href')).toBe(evidenceDownloadUrl('run', artifact.id))
    expect(link?.getAttribute('download')).toBe(artifact.name)
    expect(evidenceTab.getAttribute('aria-pressed')).toBe('true')
    expect(root.querySelector('#run-section')?.getAttribute('aria-labelledby')).toBe(
      'run-section-title',
    )
  })

  it.each(['success', 'partial', 'failed', 'cancelled', 'expired'])(
    'shows the %s terminal outcome and summary without a result',
    async (outcome) => {
      history.replaceState({}, '', '/runs/run')
      vi.mocked(api.getRun).mockResolvedValue(detailFixture('run', outcome))
      const root = mountApp()
      await vi.waitFor(() => expect(root.querySelector('.detail-view')).not.toBeNull())
      expect(root.textContent).toContain(outcome)
      expect(root.textContent).toContain('Summary run')
    },
  )

  it('distinguishes pending, empty and failed initial loads', async () => {
    const initial = deferred<never[]>()
    vi.mocked(api.listRuns).mockReturnValue(initial.promise)
    const root = mountApp()
    expect(root.textContent).toContain('Loading')
    expect(root.textContent).not.toContain('No runs have been published')
    initial.reject(new Error('Reader unavailable'))
    await vi.waitFor(() => expect(root.textContent).toContain('Reader unavailable'))
    expect(root.textContent).not.toContain('No runs have been published')
    vi.mocked(api.listRuns).mockResolvedValue([])
    root.querySelector<HTMLButtonElement>('.refresh')!.click()
    await vi.waitFor(() => expect(root.textContent).toContain('No runs have been published'))
  })

  it('follows index and detail history transitions using the decoded location', async () => {
    history.replaceState({}, '', '/runs/run%20id')
    vi.mocked(api.getRun).mockImplementation((id) => Promise.resolve(detailFixture(id)))
    const root = mountApp()
    await vi.waitFor(() => expect(root.textContent).toContain('Run run id'))
    history.replaceState({}, '', '/')
    window.dispatchEvent(new PopStateEvent('popstate'))
    await nextTick()
    expect(root.querySelector('.detail-view')).toBeNull()
    history.replaceState({}, '', '/runs/other')
    window.dispatchEvent(new PopStateEvent('popstate'))
    await vi.waitFor(() => expect(root.textContent).toContain('Run other'))
  })

  it('ignores an obsolete detail response after returning to the index', async () => {
    history.replaceState({}, '', '/runs/old')
    const old = deferred<ReturnType<typeof detailFixture>>()
    vi.mocked(api.getRun).mockReturnValue(old.promise)
    const root = mountApp()
    await vi.waitFor(() => expect(api.getRun).toHaveBeenCalled())
    root.querySelector<HTMLAnchorElement>('.brand')!.click()
    old.resolve(detailFixture('old'))
    await vi.waitFor(() => expect(root.textContent).toContain('Published runs'))
    await nextTick()
    expect(root.querySelector('.detail-view')).toBeNull()
    expect(location.pathname).toBe('/')
  })

  it('ignores an obsolete error after a newer route succeeds', async () => {
    history.replaceState({}, '', '/runs/old')
    const old = deferred<ReturnType<typeof detailFixture>>()
    vi.mocked(api.getRun).mockReturnValueOnce(old.promise).mockResolvedValue(detailFixture('new'))
    const root = mountApp()
    await vi.waitFor(() => expect(api.getRun).toHaveBeenCalled())
    history.replaceState({}, '', '/runs/new')
    window.dispatchEvent(new PopStateEvent('popstate'))
    await vi.waitFor(() => expect(root.textContent).toContain('Run new'))
    old.reject(new Error('Obsolete failure'))
    await nextTick()
    await nextTick()
    expect(root.textContent).not.toContain('Obsolete failure')
    expect(root.textContent).toContain('Run new')
  })

  it('does not publish an in-flight response after unmounting', async () => {
    history.replaceState({}, '', '/runs/run')
    const pending = deferred<ReturnType<typeof detailFixture>>()
    vi.mocked(api.getRun).mockReturnValue(pending.promise)
    const root = mountApp()
    await vi.waitFor(() => expect(api.getRun).toHaveBeenCalled())
    mountedApps.pop()!.unmount()
    const signal = vi.mocked(api.getRun).mock.calls[0]?.[1]
    expect(signal?.aborted).toBe(true)
    pending.resolve(detailFixture('run'))
    await nextTick()
    expect(root.textContent).toBe('')
  })
})
