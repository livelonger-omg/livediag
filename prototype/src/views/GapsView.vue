<script setup>
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { useBlueprintStore } from '@/stores/blueprint'

const store = useBlueprintStore()

const gapDrawer = ref(false)
const gapForm = ref(null)

const statusText = { done: '已实现', building: '建设中', planned: '待建设' }
const statusType = { done: 'success', building: 'warning', planned: 'info' }

const buildProgress = computed(() => {
  const total = store.buildItems.length
  const done = store.buildItems.filter((b) => b.status === 'done').length
  return total ? Math.round((done / total) * 100) : 0
})

function phaseName(id) {
  const p = store.phases.find((x) => x.id === id)
  return p ? p.name : id
}

function openGap(g) {
  gapForm.value = { ...g }
  gapDrawer.value = true
}

function saveGap() {
  store.updateGap(gapForm.value.id, gapForm.value)
  ElMessage.success('差距分析已更新')
  gapDrawer.value = false
}

function setBuildStatus(row, val) {
  store.updateBuildItem(row.id, { status: val })
  ElMessage.success(`${row.id} 状态已更新`)
}
</script>

<template>
  <div class="gp">
    <div class="sec-line">
      <h3>服务差距（G1–G5）</h3>
      <span class="hint">按 Zeithaml 五差距模型：现状表现 → 收敛手段 → 目标</span>
    </div>

    <div class="gap-grid">
      <div v-for="g in store.gaps" :key="g.id" class="gap-card">
        <div class="g-hd">
          <b>{{ g.name }}</b>
          <el-button text type="primary" size="small" @click="openGap(g)">编辑</el-button>
        </div>
        <div class="g-def">{{ g.definition }}</div>
        <div class="g-row">
          <span class="g-tag now">现状</span>
          <p>{{ g.asIs }}</p>
        </div>
        <div class="g-row">
          <span class="g-tag ev">证据</span>
          <p class="dim">{{ g.evidence }}</p>
        </div>
        <div class="g-row">
          <span class="g-tag act">手段</span>
          <p>{{ g.action }}</p>
        </div>
        <div class="g-target">目标：{{ g.target }}</div>
      </div>
    </div>

    <div class="sec-line" style="margin-top: 8px">
      <h3>蓝图 → 建设内容</h3>
      <span class="hint">已实现 {{ buildProgress }}%，点击状态可直接切换</span>
    </div>

    <el-table :data="store.buildItems" size="small" max-height="340">
      <el-table-column label="编号" width="66">
        <template #default="{ row }"><span class="mono bid">{{ row.id }}</span></template>
      </el-table-column>
      <el-table-column label="环节" width="96">
        <template #default="{ row }"><span class="ph">{{ phaseName(row.phase) }}</span></template>
      </el-table-column>
      <el-table-column label="覆盖失效点" width="150">
        <template #default="{ row }">
          <span v-for="f in row.fails" :key="f" class="chip chip-f">{{ f }}</span>
        </template>
      </el-table-column>
      <el-table-column prop="content" label="建设内容" min-width="330" show-overflow-tooltip />
      <el-table-column prop="acceptance" label="验收口径" min-width="230" show-overflow-tooltip />
      <el-table-column label="状态" width="132">
        <template #default="{ row }">
          <el-select
            :model-value="row.status"
            size="small"
            style="width: 104px"
            @change="(v) => setBuildStatus(row, v)"
          >
            <el-option label="已实现" value="done" />
            <el-option label="建设中" value="building" />
            <el-option label="待建设" value="planned" />
          </el-select>
        </template>
      </el-table-column>
    </el-table>

    <el-drawer v-model="gapDrawer" :title="`编辑 ${gapForm?.name || ''}`" size="560px">
      <el-form v-if="gapForm" label-position="top" size="small">
        <el-form-item label="差距名称"><el-input v-model="gapForm.name" /></el-form-item>
        <el-form-item label="定义"><el-input v-model="gapForm.definition" /></el-form-item>
        <el-form-item label="现状表现"><el-input v-model="gapForm.asIs" type="textarea" :autosize="{ minRows: 2 }" /></el-form-item>
        <el-form-item label="证据来源"><el-input v-model="gapForm.evidence" /></el-form-item>
        <el-form-item label="收敛手段"><el-input v-model="gapForm.action" type="textarea" :autosize="{ minRows: 2 }" /></el-form-item>
        <el-form-item label="目标状态"><el-input v-model="gapForm.target" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button size="small" @click="gapDrawer = false">取消</el-button>
        <el-button size="small" type="primary" @click="saveGap">保存</el-button>
      </template>
    </el-drawer>
  </div>
</template>

<style scoped>
.gp { display: flex; flex-direction: column; gap: 12px; }
.sec-line { display: flex; align-items: baseline; gap: 10px; }
.sec-line h3 { margin: 0; font-size: 14px; }
.hint { font-size: 11.5px; color: var(--dim); }
.gap-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(420px, 1fr)); gap: 12px; }
.gap-card { background: var(--panel); border: 1px solid var(--line); border-radius: 12px; padding: 13px 16px; }
.g-hd { display: flex; justify-content: space-between; align-items: center; }
.g-hd b { font-size: 13.5px; color: var(--brand); }
.g-def { font-size: 11.5px; color: var(--dim); margin: 3px 0 10px; }
.g-row { display: flex; gap: 8px; margin-bottom: 7px; font-size: 12px; line-height: 1.7; }
.g-row p { margin: 0; color: #d0d9e4; }
.g-row p.dim { color: var(--sub); }
.g-tag {
  flex: 0 0 auto; font-size: 10px; padding: 1px 6px; border-radius: 4px; height: 18px; line-height: 16px;
  border: 1px solid var(--line); color: var(--sub); background: var(--panel-2);
}
.g-tag.now { color: #ff9e86; border-color: rgba(226, 96, 60, .38); background: rgba(226, 96, 60, .1); }
.g-tag.ev { color: #e8bd82; border-color: rgba(212, 145, 63, .35); background: rgba(212, 145, 63, .1); }
.g-tag.act { color: #7fd6ab; border-color: rgba(47, 174, 122, .32); background: rgba(47, 174, 122, .1); }
.g-target { margin-top: 10px; padding-top: 8px; border-top: 1px dashed var(--line); font-size: 11.5px; color: #7fd6ab; }
.bid { color: var(--brand); font-size: 12px; }
.ph { font-size: 12px; color: var(--brand); }
</style>
