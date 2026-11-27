<script setup>
import { ref, reactive, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { useBlueprintStore } from '@/stores/blueprint'

const store = useBlueprintStore()

const filters = reactive({ phase: '', status: '', auto: '' })
const drawerVisible = ref(false)
const isNew = ref(false)
const form = ref(null)

const statusMap = {
  done: { text: '已消除', type: 'success', dot: 'done' },
  building: { text: '建设中', type: 'warning', dot: 'building' },
  planned: { text: '待建设', type: 'info', dot: 'planned' }
}
const autoMap = { 高: 'hi', 中: 'mid', 低: 'lo' }

const filtered = computed(() =>
  store.failPoints.filter((f) => {
    if (filters.phase && f.phase !== filters.phase) return false
    if (filters.status && f.status !== filters.status) return false
    if (filters.auto && f.auto !== filters.auto) return false
    return true
  })
)

const summary = computed(() => {
  const total = store.failPoints.length
  const done = store.failPoints.filter((f) => f.status === 'done').length
  return { total, done, building: store.failPoints.filter((f) => f.status === 'building').length }
})

function phaseName(id) {
  return store.phases.find((p) => p.id === id)?.name || id
}

function openEdit(row) {
  isNew.value = false
  form.value = { ...row }
  drawerVisible.value = true
}

function openNew() {
  isNew.value = true
  form.value = {
    id: '', phase: store.phases[0].id, name: '', consequence: '', cause: '',
    auto: '中', status: 'planned', solution: '', acceptance: '', owner: ''
  }
  drawerVisible.value = true
}

function save() {
  if (!form.value.name.trim()) {
    ElMessage.warning('请填写失效点名称')
    return
  }
  if (isNew.value) {
    const created = store.addFailPoint(form.value)
    ElMessage.success(`已新增 ${created.id}`)
  } else {
    store.updateFailPoint(form.value.id, form.value)
    ElMessage.success('已保存')
  }
  drawerVisible.value = false
}

function remove(row) {
  store.removeFailPoint(row.id)
  ElMessage.success(`已删除 ${row.id}`)
}
</script>

<template>
  <div class="fp">
    <div class="fp-bar">
      <div class="fp-sum">
        <span class="big">{{ summary.total }}</span>
        <span class="lbl">个失效点</span>
        <span class="sep">·</span>
        <span class="ok">已消除 {{ summary.done }}</span>
        <span class="sep">·</span>
        <span class="mid">建设中 {{ summary.building }}</span>
      </div>
      <div class="fp-filters">
        <el-select v-model="filters.phase" placeholder="全部阶段" clearable size="small" style="width: 132px">
          <el-option v-for="p in store.phases" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-select v-model="filters.status" placeholder="全部状态" clearable size="small" style="width: 118px">
          <el-option label="已消除" value="done" />
          <el-option label="建设中" value="building" />
          <el-option label="待建设" value="planned" />
        </el-select>
        <el-select v-model="filters.auto" placeholder="自动化性" clearable size="small" style="width: 112px">
          <el-option label="高" value="高" />
          <el-option label="中" value="中" />
          <el-option label="低" value="低" />
        </el-select>
        <el-button type="primary" size="small" @click="openNew">新增失效点</el-button>
      </div>
    </div>

    <el-table :data="filtered" size="small" max-height="calc(100vh - 300px)" class="fp-table" row-key="id">
      <el-table-column label="编号" width="70" fixed>
        <template #default="{ row }"><span class="chip chip-f">{{ row.id }}</span></template>
      </el-table-column>
      <el-table-column label="阶段" width="96">
        <template #default="{ row }"><span class="ph">{{ phaseName(row.phase) }}</span></template>
      </el-table-column>
      <el-table-column prop="name" label="失效点" min-width="210" show-overflow-tooltip />
      <el-table-column prop="consequence" label="业务后果" min-width="170" show-overflow-tooltip />
      <el-table-column prop="cause" label="根因" min-width="190" show-overflow-tooltip />
      <el-table-column label="自动化性" width="92" align="center">
        <template #default="{ row }">
          <span class="auto-pill" :class="autoMap[row.auto]">{{ row.auto }}</span>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="102">
        <template #default="{ row }">
          <span class="st"><i class="status-dot" :class="statusMap[row.status].dot" />{{ statusMap[row.status].text }}</span>
        </template>
      </el-table-column>
      <el-table-column prop="solution" label="目标态（消除手段）" min-width="260" show-overflow-tooltip />
      <el-table-column label="操作" width="128" fixed="right">
        <template #default="{ row }">
          <el-button text type="primary" size="small" @click="openEdit(row)">编辑</el-button>
          <el-popconfirm title="确认删除该失效点？" width="200" @confirm="remove(row)">
            <template #reference>
              <el-button text type="danger" size="small">删除</el-button>
            </template>
          </el-popconfirm>
        </template>
      </el-table-column>
    </el-table>

    <el-drawer v-model="drawerVisible" :title="isNew ? '新增失效点' : `编辑 ${form?.id}`" size="620px">
      <el-form v-if="form" label-position="top" size="small">
        <div class="grid2">
          <el-form-item label="编号">
            <el-input v-model="form.id" :disabled="!isNew" placeholder="自动生成" />
          </el-form-item>
          <el-form-item label="所属阶段">
            <el-select v-model="form.phase" style="width: 100%">
              <el-option v-for="p in store.phases" :key="p.id" :label="`${p.no} ${p.name}`" :value="p.id" />
            </el-select>
          </el-form-item>
        </div>
        <el-form-item label="失效点">
          <el-input v-model="form.name" placeholder="服务在此处容易失败的地方" />
        </el-form-item>
        <div class="grid2">
          <el-form-item label="业务后果">
            <el-input v-model="form.consequence" type="textarea" :autosize="{ minRows: 2 }" />
          </el-form-item>
          <el-form-item label="根因">
            <el-input v-model="form.cause" type="textarea" :autosize="{ minRows: 2 }" />
          </el-form-item>
        </div>
        <div class="grid3">
          <el-form-item label="可自动化性">
            <el-select v-model="form.auto" style="width: 100%">
              <el-option label="高" value="高" />
              <el-option label="中" value="中" />
              <el-option label="低" value="低" />
            </el-select>
          </el-form-item>
          <el-form-item label="当前状态">
            <el-select v-model="form.status" style="width: 100%">
              <el-option label="已消除" value="done" />
              <el-option label="建设中" value="building" />
              <el-option label="待建设" value="planned" />
            </el-select>
          </el-form-item>
          <el-form-item label="责任方">
            <el-input v-model="form.owner" />
          </el-form-item>
        </div>
        <el-form-item label="目标态（消除手段）">
          <el-input v-model="form.solution" type="textarea" :autosize="{ minRows: 2 }" />
        </el-form-item>
        <el-form-item label="验收口径">
          <el-input v-model="form.acceptance" type="textarea" :autosize="{ minRows: 2 }" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button size="small" @click="drawerVisible = false">取消</el-button>
        <el-button size="small" type="primary" @click="save">{{ isNew ? '创建' : '保存' }}</el-button>
      </template>
    </el-drawer>
  </div>
</template>

<style scoped>
.fp { display: flex; flex-direction: column; gap: 12px; }
.fp-bar { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
.fp-sum { font-size: 12px; color: var(--sub); display: flex; align-items: baseline; gap: 6px; }
.fp-sum .big { font-size: 22px; font-weight: 700; color: var(--text); }
.fp-sum .ok { color: #7fd6ab; }
.fp-sum .mid { color: #e8bd82; }
.fp-sum .sep { color: var(--dim); }
.fp-filters { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.ph { font-size: 12px; color: var(--brand); }
.auto-pill { font-size: 10.5px; padding: 1px 8px; border-radius: 999px; }
.auto-pill.hi { background: rgba(226, 96, 60, .14); border: 1px solid rgba(226, 96, 60, .36); color: #ff9e86; }
.auto-pill.mid { background: rgba(212, 145, 63, .13); border: 1px solid rgba(212, 145, 63, .34); color: #e8bd82; }
.auto-pill.lo { background: rgba(139, 152, 168, .12); border: 1px solid rgba(139, 152, 168, .3); color: #aeb9c7; }
.st { display: inline-flex; align-items: center; font-size: 12px; }
.grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 0 14px; }
.grid3 { display: grid; grid-template-columns: repeat(3, 1fr); gap: 0 14px; }
</style>
