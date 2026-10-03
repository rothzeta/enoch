export type Run = Readonly<{
  id: string
  title?: string
  state?: string
  outcome?: string
  summary?: string
  createdAt?: string
  updatedAt?: string
  request?: unknown
  result?: unknown
}>
export type Artifact = Readonly<{
  id: string
  name: string
  mimeType: string
  length: number
  sha256: string
  createdAt: string
}>
export type RunDetail = Run &
  Readonly<{
    plans?: readonly unknown[]
    events?: readonly unknown[]
    evidence?: readonly Artifact[]
    artifacts?: readonly Artifact[]
  }>

export function artifactDownloadUrl(runId: string, artifactId: string): string {
  return `/api/v1/runs/${encodeURIComponent(runId)}/artifacts/${encodeURIComponent(artifactId)}`
}

export function evidenceDownloadUrl(runId: string, evidenceId: string): string {
  return `/api/v1/runs/${encodeURIComponent(runId)}/evidence/${encodeURIComponent(evidenceId)}`
}

async function get(path: string, signal?: AbortSignal): Promise<unknown> {
  const response = await fetch(`/api/v1${path}`, {
    headers: { Accept: 'application/json' },
    signal,
  })
  if (!response.ok) throw new Error(`Request failed (${response.status})`)
  return response.json() as Promise<unknown>
}

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function optionalString(value: unknown): string | undefined {
  if (value === undefined || value === null) return undefined
  if (typeof value !== 'string') throw new Error('Invalid run response')
  return value
}

function manifest(value: unknown): Run {
  if (!record(value) || typeof value.id !== 'string') throw new Error('Invalid run response')
  return {
    id: value.id,
    title: optionalString(value.title),
    state: optionalString(value.state),
    outcome: optionalString(value.outcome),
    summary: optionalString(value.summary),
    createdAt: optionalString(value.createdAt),
    updatedAt: optionalString(value.updatedAt),
  }
}

function collection(value: unknown): readonly unknown[] {
  if (!Array.isArray(value)) throw new Error('Invalid run response')
  return value as unknown[]
}

function publications(value: unknown): readonly Artifact[] {
  return collection(value).map((item) => {
    if (
      !record(item) ||
      typeof item.id !== 'string' ||
      typeof item.name !== 'string' ||
      typeof item.mimeType !== 'string' ||
      typeof item.length !== 'number' ||
      !Number.isFinite(item.length) ||
      item.length < 0 ||
      typeof item.sha256 !== 'string' ||
      typeof item.createdAt !== 'string'
    )
      throw new Error('Invalid run response')
    return {
      id: item.id,
      name: item.name,
      mimeType: item.mimeType,
      length: item.length,
      sha256: item.sha256,
      createdAt: item.createdAt,
    }
  })
}

async function listRuns(signal?: AbortSignal): Promise<readonly Run[]> {
  const value = await get('/runs', signal)
  const items = record(value) ? value.items : value
  return collection(items).map(manifest)
}

async function getRun(id: string, signal?: AbortSignal): Promise<RunDetail> {
  const value = await get(`/runs/${encodeURIComponent(id)}`, signal)
  if (!record(value)) throw new Error('Invalid run response')
  return {
    ...manifest(value.manifest),
    request: value.request,
    plans: collection(value.plans),
    events: collection(value.events),
    result: value.result,
    evidence: publications(value.evidence),
    artifacts: publications(value.artifacts),
  }
}

export const api = {
  listRuns,
  getRun,
  getPlans: async (id: string) => collection(await get(`/runs/${encodeURIComponent(id)}/plans`)),
  getEvents: async (id: string) => collection(await get(`/runs/${encodeURIComponent(id)}/events`)),
  getEvidence: async (id: string) =>
    publications(await get(`/runs/${encodeURIComponent(id)}/evidence`)),
  getArtifacts: async (id: string) =>
    publications(await get(`/runs/${encodeURIComponent(id)}/artifacts`)),
}
