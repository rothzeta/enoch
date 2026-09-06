<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { api, artifactDownloadUrl, type Run, type RunDetail } from './api'

const runs = ref<Run[]>([])
const selected = ref<RunDetail | null>(null)
const error = ref('')
const loading = ref(false)
const routeId = ref(location.pathname.match(/^\/runs\/([^/]+)/)?.[1] ?? '')
let timer: ReturnType<typeof setInterval> | undefined

const normalize = (value: Run[] | { items: Run[] }) => Array.isArray(value) ? value : value.items
const detail = computed(() => selected.value)
const tab = ref('overview')
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
  try {
    error.value = ''
    runs.value = normalize(await api.listRuns())
    if (routeId.value) {
      const raw = await api.getRun(routeId.value)
      // The API returns a RunDocument with its lifecycle data under `manifest`.
      // Flatten that envelope for the view while retaining the full collections.
      const envelope = raw as RunDetail & { manifest?: Run }
      selected.value = envelope.manifest ? { ...envelope.manifest, ...envelope } : envelope
    }
  } catch (e) { error.value = e instanceof Error ? e.message : 'Unable to load runs' }
}
function openRun(id: string) { routeId.value = id; history.pushState({}, '', `/runs/${encodeURIComponent(id)}`); tab.value = 'overview'; void refresh() }
function back() { routeId.value = ''; selected.value = null; history.pushState({}, '', '/'); void refresh() }
function display(value: unknown): string { return value === undefined || value === null ? '—' : typeof value === 'string' ? value : JSON.stringify(value, null, 2) }
function label(value: unknown): string { return String(value ?? 'unknown').replaceAll('_', ' ') }
onMounted(() => { window.addEventListener('popstate', refresh); void refresh(); timer = setInterval(refresh, 7000) })
onUnmounted(() => { window.removeEventListener('popstate', refresh); if (timer) clearInterval(timer) })
</script>

<template>
  <header class="topbar"><a class="brand" href="/" @click.prevent="back">ENOCH<span>/</span>RUNS</a><span class="tagline">durable agent work records</span><button class="refresh" @click="refresh">Refresh</button></header>
  <main class="shell">
    <div v-if="error" class="error" role="alert">{{ error }}</div>
    <section v-if="!detail" class="list-view"><div class="heading"><div><p class="eyebrow">RUN INDEX</p><h1>Published runs</h1></div><span class="count">{{ runs.length }} total</span></div><div v-if="loading" class="muted">Loading…</div><div v-else-if="!runs.length" class="empty">No runs have been published yet.</div><button v-for="run in runs" :key="String(run.id)" class="run-card" @click="openRun(String(run.id))"><div><strong>{{ run.title || run.id }}</strong><small>{{ run.id }}</small></div><div class="run-meta"><span :class="['pill', String(run.state || '').toLowerCase()]">{{ label(run.state || 'unknown') }}</span><time>{{ run.updatedAt || run.createdAt || '' }}</time></div></button></section>
    <section v-else class="detail-view"><button class="back" @click="back">← All runs</button><div class="detail-head"><div><p class="eyebrow">RUN / {{ detail.id }}</p><h1>{{ detail.title || detail.id }}</h1></div><span :class="['pill', String(detail.state || '').toLowerCase()]">{{ label(detail.state || 'unknown') }}</span></div><nav class="tabs" aria-label="Run sections"><button v-for="item in ['overview','plan','progress','evidence','artifacts','result']" :key="item" :class="{ active: tab === item }" @click="tab = item">{{ item }}</button></nav><article class="panel"><h2>{{ tab }}</h2><template v-if="tab === 'artifacts'"><div v-if="detail.artifacts?.length" class="artifact-list"><section v-for="artifact in detail.artifacts" :key="artifact.id" class="artifact-card"><div class="artifact-head"><strong>{{ artifact.name }}</strong><a :href="artifactDownloadUrl(detail.id, artifact.id)" :download="artifact.name">Download</a></div><dl><div><dt>MIME type</dt><dd>{{ artifact.mimeType }}</dd></div><div><dt>Byte length</dt><dd>{{ artifact.length }} bytes</dd></div><div><dt>SHA-256</dt><dd class="checksum">{{ artifact.sha256 }}</dd></div></dl></section></div><p v-else class="muted">No artifacts published.</p></template><template v-else><pre v-if="dataForTab !== null && dataForTab !== undefined">{{ display(dataForTab) }}</pre><p v-else class="muted">No {{ tab }} published.</p></template></article></section>
  </main>
  <footer>Polling every 7 seconds · filesystem-backed records · read-only browser</footer>
</template>
