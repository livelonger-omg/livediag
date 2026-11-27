<script setup>
import { computed } from 'vue'
import { useBlueprintStore } from '@/stores/blueprint'

const props = defineProps({
  mode: { type: String, default: 'compare' }
})
const emit = defineEmits(['cell-click'])

const store = useBlueprintStore()

const gridStyle = computed(() => ({
  gridTemplateColumns: `var(--label-w) repeat(${store.phases.length}, var(--col-w))`
}))

function dividerAfter(layerId) {
  return store.dividers.find((d) => d.after === layerId)
}

function cell(phaseId, layerId) {
  return store.cellOf(phaseId, layerId)
}

function failChips(phaseId, layerId) {
  const c = cell(phaseId, layerId)
  return Array.isArray(c.f) ? c.f : []
}

function waitChips(phaseId, layerId) {
  const c = cell(phaseId, layerId)
  return Array.isArray(c.w) ? c.w : []
}

function metricCount(phaseId) {
  return store.metrics.filter((m) => m.phase === phaseId).length
}

function empty(c) {
  const a = (c.asIs || []).filter(Boolean).length
  const b = (c.toBe || []).filter(Boolean).length
  return !a && !b
}

function cut(list, n) {
  const arr = Array.isArray(list) ? list : []
  return arr.slice(0, n)
}

function more(list, n) {
  const arr = Array.isArray(list) ? list : []
  return Math.max(0, arr.length - n)
}
</script>

<template>
  <div class="bp-scroller">
    <div class="bp-grid" :style="gridStyle">
      <!-- 阶段表头 -->
      <div class="bp-corner">阶段 / Phase</div>
      <div v-for="p in store.phases" :key="p.id" class="bp-head">
        <div class="head-no">{{ p.no }}</div>
        <div class="head-nm">{{ p.name }}</div>
        <div class="head-sla">{{ p.sla }}</div>
        <div class="head-goal">{{ p.goal }}</div>
        <div class="head-meta">
          <span class="mini">{{ store.failsOfPhase(p.id).length }} 个失效点</span>
          <span class="mini">{{ metricCount(p.id) }} 项指标</span>
        </div>
      </div>

      <!-- 泳道 -->
      <template v-for="layer in store.layers" :key="layer.id">
        <div class="bp-label" :class="`tone-${layer.tone}`">
          <b>{{ layer.name }}</b>
          <span>{{ layer.en }}</span>
        </div>

        <div
          v-for="p in store.phases"
          :key="`${p.id}-${layer.id}`"
          class="bp-cell"
          :class="{ empty: empty(cell(p.id, layer.id)) }"
          @click="emit('cell-click', { phaseId: p.id, layerId: layer.id })"
        >
          <div class="tags">
            <span v-for="f in failChips(p.id, layer.id)" :key="f" class="chip chip-f">{{ f }}</span>
            <span v-for="w in waitChips(p.id, layer.id)" :key="w" class="chip chip-w">{{ w }}</span>
          </div>

          <template v-if="mode !== 'toBe'">
            <ul class="lst asis">
              <li v-for="(t, i) in cut(cell(p.id, layer.id).asIs, mode === 'asIs' ? 99 : 3)" :key="`a${i}`">{{ t }}</li>
              <li v-if="more(cell(p.id, layer.id).asIs, mode === 'asIs' ? 99 : 3) > 0" class="more">
                +{{ more(cell(p.id, layer.id).asIs, mode === 'asIs' ? 99 : 3) }} 条
              </li>
            </ul>
          </template>

          <template v-if="mode !== 'asIs'">
            <ul class="lst tobe">
              <li v-for="(t, i) in cut(cell(p.id, layer.id).toBe, mode === 'toBe' ? 99 : 4)" :key="`b${i}`">{{ t }}</li>
              <li v-if="more(cell(p.id, layer.id).toBe, mode === 'toBe' ? 99 : 4) > 0" class="more">
                +{{ more(cell(p.id, layer.id).toBe, mode === 'toBe' ? 99 : 4) }} 条
              </li>
            </ul>
          </template>

          <div v-if="empty(cell(p.id, layer.id))" class="empty-hint">点击补充</div>
        </div>

        <!-- 分界线 -->
        <div v-if="dividerAfter(layer.id)" class="bp-divider">
          <div class="dv-label">
            <b>{{ dividerAfter(layer.id).name }}</b>
            <span>{{ dividerAfter(layer.id).en }}</span>
          </div>
          <div class="dv-line">
            <i />
            <span>{{ dividerAfter(layer.id).desc }}</span>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.bp-scroller {
  --label-w: 148px;
  --col-w: 242px;
  flex: 1 1 auto;
  min-height: 320px;
  overflow: auto;
  border: 1px solid var(--line);
  border-radius: 14px;
  background: var(--panel);
}
.bp-grid {
  display: grid;
  min-width: max-content;
  align-items: stretch;
}

.bp-corner,
.bp-head,
.bp-label,
.bp-cell {
  border-right: 1px solid var(--line);
  border-bottom: 1px solid var(--line);
}

/* ---------- 表头 ---------- */
.bp-corner {
  position: sticky;
  left: 0;
  top: 0;
  z-index: 8;
  background: var(--panel-3);
  padding: 10px 13px;
  font-size: 11.5px;
  color: var(--sub);
  display: flex;
  align-items: center;
}
.bp-head {
  position: sticky;
  top: 0;
  z-index: 6;
  background: linear-gradient(180deg, rgba(0, 160, 233, .14), rgba(0, 160, 233, .02)), var(--panel-2);
  padding: 10px 13px;
}
.head-no { font-size: 10.5px; color: var(--brand); letter-spacing: .08em; }
.head-nm { font-size: 14px; font-weight: 700; margin-top: 3px; }
.head-sla { font-size: 10.5px; color: #7fd0f5; margin-top: 3px; }
.head-goal { font-size: 11px; color: var(--sub); margin-top: 6px; line-height: 1.6; }
.head-meta { display: flex; gap: 8px; margin-top: 6px; }
.mini { font-size: 10px; color: var(--dim); background: rgba(255, 255, 255, .04); padding: 1px 6px; border-radius: 4px; }

/* ---------- 行标签 ---------- */
.bp-label {
  position: sticky;
  left: 0;
  z-index: 4;
  background: var(--panel-2);
  padding: 12px 13px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 3px;
}
.bp-label b { font-size: 12.5px; }
.bp-label span { font-size: 10px; color: var(--dim); letter-spacing: .04em; }
.bp-label.tone-evi b { color: #c9afff; }
.bp-label.tone-peo b { color: #e8bd82; }
.bp-label.tone-sys b { color: #7fd0f5; }

/* ---------- 单元格 ---------- */
.bp-cell {
  padding: 8px 10px 9px;
  cursor: pointer;
  transition: background .15s;
  font-size: 11.8px;
  line-height: 1.58;
  position: relative;
}
.bp-cell:hover { background: #1a2532; box-shadow: inset 0 0 0 1px rgba(0, 160, 233, .35); }
.bp-cell.empty { background: rgba(255, 255, 255, .012); }
.tags { display: flex; flex-wrap: wrap; gap: 3px; margin-bottom: 3px; }
.tags:empty { display: none; }
.lst { margin: 0; padding-left: 13px; }
.lst li { margin-bottom: 2px; }
.lst li:last-child { margin-bottom: 0; }
.lst.asis { color: #8492a4; }
.lst.asis li::marker { color: #46536a; }
.lst.tobe { margin-top: 5px; padding-top: 5px; border-top: 1px dashed rgba(0, 160, 233, .28); }
.lst.tobe li { color: #dbe9f5; }
.lst.tobe li::marker { color: var(--brand); }
.lst li.more { list-style: none; margin-left: -13px; font-size: 10.5px; color: var(--dim); }
.empty-hint { font-size: 11px; color: var(--dim); }

/* ---------- 分界线 ---------- */
.bp-divider {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  gap: 12px;
  background: rgba(0, 160, 233, .06);
  border-bottom: 1px solid var(--line);
  padding: 6px 13px 6px 0;
}
.dv-label {
  position: sticky;
  left: 0;
  z-index: 4;
  display: flex;
  flex-direction: column;
  justify-content: center;
  width: var(--label-w);
  min-width: var(--label-w);
  padding: 3px 13px;
  background: var(--panel-2);
  border-right: 1px solid var(--line);
}
.dv-label b { font-size: 11.5px; color: var(--brand); }
.dv-label span { font-size: 9.5px; color: var(--dim); letter-spacing: .03em; }
.dv-line { flex: 1; display: flex; align-items: center; gap: 10px; min-width: 0; }
.dv-line i { flex: 1; border-top: 2px dashed rgba(0, 160, 233, .4); }
.dv-line span { font-size: 10.5px; color: var(--dim); white-space: nowrap; }
</style>
