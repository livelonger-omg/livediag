<script setup>
import { computed } from 'vue'
import { useBlueprintStore } from '@/stores/blueprint'

const props = defineProps({
  visible: { type: Boolean, default: false },
  phaseId: { type: String, default: '' },
  layerId: { type: String, default: '' }
})
const emit = defineEmits(['update:visible', 'open-fail'])

const store = useBlueprintStore()

const show = computed({
  get: () => props.visible,
  set: (v) => emit('update:visible', v)
})

const phase = computed(() => store.phases.find((p) => p.id === props.phaseId))
const layer = computed(() => store.layers.find((l) => l.id === props.layerId))
const title = computed(() =>
  phase.value && layer.value ? `${phase.value.name} · ${layer.value.name}` : '编辑单元格'
)

const asIs = computed(() => store.cellOf(props.phaseId, props.layerId).asIs || [])
const toBe = computed(() => store.cellOf(props.phaseId, props.layerId).toBe || [])

const cell = computed(() => {
  const c = store.cells[`${props.phaseId}.${props.layerId}`] || {}
  return c
})

const linkedFails = computed(() => {
  const ids = Array.isArray(cell.value.f) ? cell.value.f : []
  return ids.map((id) => store.failById(id)).filter(Boolean)
})

const linkedWaits = computed(() => {
  const ids = Array.isArray(cell.value.w) ? cell.value.w : []
  return ids.map((id) => store.waits.find((w) => w.id === id)).filter(Boolean)
})

function setItem(field, index, value) {
  store.setCellItem(props.phaseId, props.layerId, field, index, value)
}
function addItem(field) {
  store.addCellItem(props.phaseId, props.layerId, field, '')
}
function removeItem(field, index) {
  store.removeCellItem(props.phaseId, props.layerId, field, index)
}

const statusText = { done: '已消除', building: '建设中', planned: '待建设' }
</script>

<template>
  <el-drawer v-model="show" :title="title" size="580px" :append-to-body="true">
    <template #header>
      <div class="dr-head">
        <div class="dr-title">{{ title }}</div>
        <div class="dr-sub" v-if="phase">
          {{ phase.no }} · {{ phase.sla }} · 责任：{{ phase.owner }}
        </div>
      </div>
    </template>

    <div class="dr-body">
      <div class="blk">
        <div class="blk-hd">
          <span class="dot asis" />现状（As-Is）
          <span class="blk-hint">客户与一线当前真实发生的情况</span>
        </div>
        <div class="item-list">
          <div v-for="(t, i) in asIs" :key="`a${i}`" class="item-row">
            <el-input
              :model-value="t"
              size="small"
              placeholder="描述现状动作或证据"
              @update:model-value="(v) => setItem('asIs', i, v)"
            />
            <el-button text type="danger" size="small" @click="removeItem('asIs', i)">删除</el-button>
          </div>
          <div v-if="!asIs.length" class="empty-tip">暂无现状条目</div>
        </div>
        <el-button text type="primary" size="small" @click="addItem('asIs')">+ 添加现状条目</el-button>
      </div>

      <div class="blk">
        <div class="blk-hd">
          <span class="dot tobe" />目标态（To-Be）
          <span class="blk-hint">引入 LiveDiag 后应达到的状态</span>
        </div>
        <div class="item-list">
          <div v-for="(t, i) in toBe" :key="`b${i}`" class="item-row">
            <el-input
              :model-value="t"
              size="small"
              placeholder="描述目标动作、系统或验收标准"
              @update:model-value="(v) => setItem('toBe', i, v)"
            />
            <el-button text type="danger" size="small" @click="removeItem('toBe', i)">删除</el-button>
          </div>
          <div v-if="!toBe.length" class="empty-tip">暂无目标态条目</div>
        </div>
        <el-button text type="primary" size="small" @click="addItem('toBe')">+ 添加目标条目</el-button>
      </div>

      <div class="blk">
        <div class="blk-hd">关联标注</div>
        <div class="sel-row">
          <span class="sel-lb">失效点</span>
          <el-select
            :model-value="cell.f || []"
            multiple
            collapse-tags
            collapse-tags-tooltip
            size="small"
            placeholder="选择该格关联的失效点"
            style="flex: 1"
            @change="(v) => store.setCellTags(phaseId, layerId, 'f', v)"
          >
            <el-option v-for="f in store.failPoints" :key="f.id" :label="`${f.id} ${f.name}`" :value="f.id" />
          </el-select>
        </div>
        <div class="sel-row">
          <span class="sel-lb">等待点</span>
          <el-select
            :model-value="cell.w || []"
            multiple
            collapse-tags
            collapse-tags-tooltip
            size="small"
            placeholder="选择该格关联的等待点"
            style="flex: 1"
            @change="(v) => store.setCellTags(phaseId, layerId, 'w', v)"
          >
            <el-option v-for="w in store.waits" :key="w.id" :label="`${w.id} ${w.desc}`" :value="w.id" />
          </el-select>
        </div>
        <div v-if="linkedFails.length" class="rel-list">
          <div
            v-for="f in linkedFails"
            :key="f.id"
            class="rel-item"
            @click="emit('open-fail', f.id)"
          >
            <span class="chip chip-f">{{ f.id }}</span>
            <span class="rel-name">{{ f.name }}</span>
            <span class="rel-status" :class="f.status">{{ statusText[f.status] }}</span>
          </div>
        </div>
        <div v-if="linkedWaits.length" class="rel-list">
          <div v-for="w in linkedWaits" :key="w.id" class="rel-item plain">
            <span class="chip chip-w">{{ w.id }}</span>
            <span class="rel-name">{{ w.desc }}</span>
          </div>
        </div>
      </div>
    </div>

    <template #footer>
      <el-button size="small" @click="show = false">完成</el-button>
    </template>
  </el-drawer>
</template>

<style scoped>
.dr-head { padding: 4px 0; }
.dr-title { font-size: 16px; font-weight: 700; }
.dr-sub { font-size: 11.5px; color: var(--sub); margin-top: 4px; }
.dr-body { display: flex; flex-direction: column; gap: 22px; }
.blk-hd { font-size: 12.5px; font-weight: 600; margin-bottom: 9px; display: flex; align-items: center; gap: 7px; }
.blk-hint { font-weight: 400; font-size: 11px; color: var(--dim); }
.dot { width: 7px; height: 7px; border-radius: 50%; display: inline-block; }
.dot.asis { background: #5d6b7d; }
.dot.tobe { background: var(--brand); }
.item-list { display: flex; flex-direction: column; gap: 7px; margin-bottom: 6px; }
.sel-row { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
.sel-lb { font-size: 11.5px; color: var(--sub); flex: 0 0 42px; }
.rel-list { display: flex; flex-direction: column; gap: 6px; margin-bottom: 8px; }
.rel-item {
  display: flex; align-items: center; gap: 8px; padding: 8px 10px;
  border: 1px solid var(--line); border-radius: 8px; background: var(--panel-2);
  cursor: pointer; font-size: 12px;
}
.rel-item:hover { border-color: rgba(0, 160, 233, .5); }
.rel-item.plain { cursor: default; }
.rel-name { flex: 1; color: #d6dee8; }
.rel-status { font-size: 10.5px; color: var(--sub); }
.rel-status.done { color: #7fd6ab; }
.rel-status.building { color: #e8bd82; }
.rel-status.planned { color: var(--dim); }
</style>
