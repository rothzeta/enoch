<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { api, artifactDownloadUrl, evidenceDownloadUrl, type Run, type RunDetail } from './api'

const runs = ref<readonly Run[]>([])
const selected = ref<RunDetail | null>(null)
const error = ref('')
const loading = ref(true)
const routeId = ref('')
let timer: ReturnType<typeof setInterval> | undefined
let activeRequest: AbortController | undefined
let requestVersion = 0
let unmounted = false

const detail = computed(() => selected.value)
const tab = ref('overview')
const sections = ['overview', 'plan', 'progress', 'evidence', 'artifacts', 'result'] as const
const files = computed(() =>
  tab.value === 'evidence' ? detail.value?.evidence : detail.value?.artifacts,
)
const dataForTab = computed(() => {
  const d = detail.value
  if (!d) return null
  if (tab.value === 'plan') return d.plans
  if (tab.value === 'progress') return d.events
  if (tab.value === 'evidence') return d.evidence
  if (tab.value === 'artifacts') return d.artifacts
  if (tab.value === 'result') return d.result
  return d.request
})

async function refresh() {
  activeRequest?.abort()
  const controller = new AbortController()
  activeRequest = controller
  const version = ++requestVersion
  const id = routeId.value
  const current = () => !unmounted && version === requestVersion && id === routeId.value
  loading.value = true
  error.value = ''
  try {
    const [nextRuns, nextSelected] = await Promise.all([
      api.listRuns(controller.signal),
      id ? api.getRun(id, controller.signal) : Promise.resolve(null),
    ])
    if (!current()) return
    runs.value = nextRuns
    selected.value = nextSelected
  } catch (e) {
    if (!current()) return
    error.value = e instanceof Error ? e.message : 'Unable to load runs'
  } finally {
    if (current()) loading.value = false
  }
}
function followLocation() {
  const encoded = location.pathname.match(/^\/runs\/([^/]+)\/?$/)?.[1]
  try {
    const id = encoded ? decodeURIComponent(encoded) : ''
    if (id !== routeId.value) {
      routeId.value = id
      selected.value = null
      tab.value = 'overview'
    }
    void refresh()
  } catch {
    activeRequest?.abort()
    requestVersion++
    selected.value = null
    routeId.value = ''
    loading.value = false
    error.value = 'Invalid run address'
  }
}
function openRun(id: string) {
  history.pushState({}, '', `/runs/${encodeURIComponent(id)}`)
  followLocation()
}
function back() {
  history.pushState({}, '', '/')
  followLocation()
}
function display(value: unknown): string {
  return value === undefined || value === null
    ? '—'
    : typeof value === 'string'
      ? value
      : JSON.stringify(value, null, 2)
}
function label(value: string): string {
  return String(value ?? 'unknown').replaceAll('_', ' ')
}
function refreshInBackground(): void {
  if (loading.value) return
  // refresh catches and exposes failures in the component's error state.
  void refresh()
}
onMounted(() => {
  window.addEventListener('popstate', followLocation)
  followLocation()
  timer = setInterval(refreshInBackground, 7000)
})
onUnmounted(() => {
  unmounted = true
  activeRequest?.abort()
  window.removeEventListener('popstate', followLocation)
  if (timer) clearInterval(timer)
})
</script>

<template>
  <header class="topbar">
    <a class="brand" href="/" @click.prevent="back">ENOCH<span>/</span>RUNS</a
    ><span class="tagline">durable agent work records</span
    ><button class="refresh" @click="refresh">Refresh</button>
  </header>
  <main class="shell">
    <div v-if="error" class="error" role="alert">{{ error }}</div>
    <div v-if="routeId && !detail && loading" class="muted" role="status">Loading run…</div>
    <section v-if="!routeId" class="list-view">
      <div class="heading">
        <div>
          <p class="eyebrow">RUN INDEX</p>
          <h1>Published runs</h1>
        </div>
        <span class="count">{{ runs.length }} total</span>
      </div>
      <div v-if="loading" class="muted" role="status">Loading…</div>
      <div v-else-if="!error && !runs.length" class="empty">No runs have been published yet.</div>
      <button
        v-for="run in runs"
        :key="String(run.id)"
        class="run-card"
        @click="openRun(String(run.id))"
      >
        <div>
          <strong>{{ run.title || run.id }}</strong
          ><small>{{ run.id }}</small>
        </div>
        <div class="run-meta">
          <span :class="['pill', String(run.state || '').toLowerCase()]">{{
            label(run.state || 'unknown')
          }}</span
          ><time :datetime="run.updatedAt || run.createdAt">{{
            run.updatedAt || run.createdAt || ''
          }}</time>
        </div>
      </button>
    </section>
    <section v-else-if="detail" class="detail-view" :aria-busy="loading">
      <button class="back" @click="back">← All runs</button>
      <div class="detail-head">
        <div>
          <p class="eyebrow">RUN / {{ detail.id }}</p>
          <h1>{{ detail.title || detail.id }}</h1>
        </div>
        <div class="run-meta">
          <span :class="['pill', String(detail.state || '').toLowerCase()]">{{
            label(detail.state || 'unknown')
          }}</span>
          <span v-if="detail.outcome" :class="['pill', detail.outcome.toLowerCase()]">{{
            label(detail.outcome)
          }}</span>
        </div>
      </div>
      <p v-if="detail.summary" class="run-summary">{{ detail.summary }}</p>
      <nav class="tabs" aria-label="Run sections">
        <button
          v-for="item in sections"
          :key="item"
          :class="{ active: tab === item }"
          :aria-pressed="tab === item"
          aria-controls="run-section"
          @click="tab = item"
        >
          {{ item }}
        </button>
      </nav>
      <article id="run-section" class="panel" aria-labelledby="run-section-title">
        <h2 id="run-section-title">{{ tab }}</h2>
        <template v-if="tab === 'artifacts' || tab === 'evidence'"
          ><div v-if="files?.length" class="artifact-list">
            <section v-for="artifact in files" :key="artifact.id" class="artifact-card">
              <div class="artifact-head">
                <strong>{{ artifact.name }}</strong
                ><a
                  :href="
                    tab === 'evidence'
                      ? evidenceDownloadUrl(detail.id, artifact.id)
                      : artifactDownloadUrl(detail.id, artifact.id)
                  "
                  :download="artifact.name"
                  >Download</a
                >
              </div>
              <dl>
                <div>
                  <dt>MIME type</dt>
                  <dd>{{ artifact.mimeType }}</dd>
                </div>
                <div>
                  <dt>Byte length</dt>
                  <dd>{{ artifact.length }} bytes</dd>
                </div>
                <div>
                  <dt>SHA-256</dt>
                  <dd class="checksum">{{ artifact.sha256 }}</dd>
                </div>
              </dl>
            </section>
          </div>
          <p v-else class="muted">No {{ tab }} published.</p></template
        ><template v-else>
          <pre v-if="dataForTab !== null && dataForTab !== undefined">{{
            display(dataForTab)
          }}</pre>
          <p v-else class="muted">No {{ tab }} published.</p></template
        >
      </article>
    </section>
  </main>
  <footer>Polling every 7 seconds · filesystem-backed records · read-only browser</footer>
</template>
