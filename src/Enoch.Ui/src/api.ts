export type Run = { id: string; title?: string; state?: string; outcome?: string; createdAt?: string; updatedAt?: string; request?: unknown; result?: unknown; [key: string]: unknown }
export type Artifact = { id: string; name: string; mimeType: string; length: number; sha256: string; createdAt: string }
export type RunDetail = Run & { plans?: unknown[]; events?: unknown[]; evidence?: unknown[]; artifacts?: Artifact[] }

export function artifactDownloadUrl(runId: string, artifactId: string): string {
  return `/api/v1/runs/${encodeURIComponent(runId)}/artifacts/${encodeURIComponent(artifactId)}`
}

async function get<T>(path: string): Promise<T> {
  const response = await fetch(`/api/v1${path}`, { headers: { Accept: 'application/json' } })
  if (!response.ok) throw new Error(`Request failed (${response.status})`)
  return response.json() as Promise<T>
}

export const api = {
  listRuns: () => get<Run[] | { items: Run[] }>('/runs'),
  getRun: (id: string) => get<RunDetail>(`/runs/${encodeURIComponent(id)}`),
  getPlans: (id: string) => get<unknown[]>(`/runs/${encodeURIComponent(id)}/plans`),
  getEvents: (id: string) => get<unknown[]>(`/runs/${encodeURIComponent(id)}/events`),
  getEvidence: (id: string) => get<unknown[]>(`/runs/${encodeURIComponent(id)}/evidence`),
  getArtifacts: (id: string) => get<Artifact[]>(`/runs/${encodeURIComponent(id)}/artifacts`),
}
