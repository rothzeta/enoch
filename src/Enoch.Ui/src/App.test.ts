// @vitest-environment jsdom

import { createApp, nextTick } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App.vue'
import { artifactDownloadUrl } from './api'
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

afterEach(() => {
  document.body.innerHTML = ''
  history.replaceState({}, '', '/')
  vi.restoreAllMocks()
})

describe('artifact downloads', () => {
  it('constructs a same-origin URL with encoded route segments', () => {
    expect(artifactDownloadUrl('run /?#', 'artifact /?#')).toBe(
      '/api/v1/runs/run%20%2F%3F%23/artifacts/artifact%20%2F%3F%23',
    )
  })

  it('renders artifact metadata and a visible download link', async () => {
    history.replaceState({}, '', '/runs/run-id')
    const root = document.createElement('div')
    document.body.append(root)
    const app = createApp(App)
    app.mount(root)
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

    app.unmount()
  })
})
