<script setup>
import { ref, computed } from 'vue'
import { useBlueprintStore } from '@/stores/blueprint'
import BlueprintGrid from '@/components/BlueprintGrid.vue'
import CellDrawer from '@/components/CellDrawer.vue'

const store = useBlueprintStore()
const emit = defineEmits(['open-tab'])

const mode = computed({
  get: () => store.ui.viewMode || 'compare',
  set: (v) => { store.ui.viewMode = v }
})

const drawer = ref({ visible: false, phaseId: '', layerId: '' })
const stats = computed(() => store.stats)

const metricConfirmed = computed(() => store.metrics.filter((m) => m.confirmed).length)

const cards = computed(() => [
  {
    label: '失效点已消除',
    value: `${stats.value.failDone} / ${stats.value.failTotal}`,
    pct: stats.value.failResolvedRate,
    note: `建设中 ${stats.value.failBuilding} · 待建设 ${stats.value.failPlanned}`,
    color: 'var(--good)'
  },
  {
    label: '单元格覆盖度',
    value: `${stats.value.cellCoverage}%`,
    pct: stats.value.cellCoverage,
    note: `${stats.value.cellsFilled} / ${stats.value.cellsTotal} 格已填内容`,
    color: 'var(--brand)'
  },
  {
    label: '指标与 SLA',
    value: `${store.metrics.length} 项`,
    pct: store.metrics.length ? Math.round((metricConfirmed.value / store.metrics.length) * 100) : 0,
    note: `口径已确认 ${metricConfirmed.value} 项`,
    color: 'var(--warn)'
  },
  {
    label: '建设内容',
    value: `${stats.value.buildDone} / ${stats.value.buildTotal}`,
    pct: stats.value.buildTotal ? Math.round((stats.value.buildDone / stats.value.buildTotal) * 100) : 0,
    note: '已实现 / 全部建设项',
    color: 'var(--purple)'
  }
])

function openCell(payload) {
  drawer.value = { visible: true, ...payload }
}

function openFail() {
  drawer.value.visible = false
  emit('open-tab', 'fails')
}
</script>

<template>
  <div class="board">
    <div class="stat-row">
      <div v-for="c in cards" :key="c.label" class="stat">
        <div class="stat-hd">{{ c.label }}</div>
        <div class="stat-val" :style="{ color: c.color }">{{ c.value }}</div>
        <el-progress
          :percentage="c.pct"
          :stroke-width="4"
          :show-text="false"
          :color="c.color"
          class="stat-bar"
        />
        <div class="stat-note">{{ c.note }}</div>
      </div>
    </div>

    <div class="insight">
      <span class="ins-tag">口径冲突</span>
      <span>结单时限 6h 与直播卡顿只在黄金档（19–23 点）复现互斥，装维只能“先体感结单、夜间复发再返工”——直播类应另设“次日复验”，不以当日结单论成败。</span>
    </div>

    <div class="toolbar">
      <div class="tb-left">
        <el-radio-group v-model="mode" size="small">
          <el-radio-button value="compare">现状 / 目标对照</el-radio-button>
          <el-radio-button value="toBe">仅目标态</el-radio-button>
          <el-radio-button value="asIs">仅现状</el-radio-button>
        </el-radio-group>
        <span class="tb-tip">点击任意单元格可编辑条目</span>
      </div>
      <div class="legend">
        <span><i class="lg lg-asis" />现状</span>
        <span><i class="lg lg-tobe" />目标态</span>
        <span class="chip chip-f">F</span><span class="lg-txt">失效点</span>
        <span class="chip chip-w">W</span><span class="lg-txt">等待点</span>
      </div>
    </div>

    <BlueprintGrid :mode="mode" @cell-click="openCell" />

    <div class="foot-note">
      {{ store.meta.origin }} ｜ 现行 SLA：受理 {{ store.meta.sla.accept }}min · 上门
      {{ store.meta.sla.arrive / 60 }}h · 结单 {{ store.meta.sla.close / 60 }}h ｜ {{ store.meta.scope }}
    </div>

    <CellDrawer
      v-model:visible="drawer.visible"
      :phase-id="drawer.phaseId"
      :layer-id="drawer.layerId"
      @open-fail="openFail"
    />
  </div>
</template>

<style scoped>
.board { display: flex; flex-direction: column; gap: 12px; height: 100%; }
.stat-row { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; }
.stat {
  background: var(--panel); border: 1px solid var(--line);
  border-radius: 12px; padding: 10px 14px;
}
.stat-hd { font-size: 11.5px; color: var(--sub); }
.stat-val { font-size: 19px; font-weight: 700; margin: 2px 0 5px; letter-spacing: -.01em; }

.insight {
  display: flex; align-items: center; gap: 10px;
  background: rgba(212, 145, 63, .08);
  border: 1px solid rgba(212, 145, 63, .28);
  border-radius: 10px; padding: 7px 12px;
  font-size: 11.8px; color: #e3cba8; line-height: 1.65;
}
.ins-tag {
  flex: 0 0 auto; font-size: 10.5px; font-weight: 700; color: #f0c07a;
  background: rgba(212, 145, 63, .16); border: 1px solid rgba(212, 145, 63, .4);
  padding: 1px 7px; border-radius: 5px;
}
.stat-bar { margin-bottom: 6px; }
.stat-note { font-size: 11px; color: var(--dim); }

.toolbar { display: flex; align-items: center; justify-content: space-between; gap: 14px; flex-wrap: wrap; }
.tb-left { display: flex; align-items: center; gap: 12px; }
.tb-tip { font-size: 11.5px; color: var(--dim); }
.legend { display: flex; align-items: center; gap: 6px; font-size: 11.5px; color: var(--sub); }
.legend > span { display: inline-flex; align-items: center; }
.lg { width: 12px; height: 3px; border-radius: 2px; display: inline-block; margin-right: 5px; }
.lg-asis { background: #5d6b7d; }
.lg-tobe { background: var(--brand); }
.lg-txt { margin-right: 8px; }

.foot-note { font-size: 11px; color: var(--dim); }

@media (max-width: 1100px) {
  .stat-row { grid-template-columns: repeat(2, 1fr); }
}
</style>
