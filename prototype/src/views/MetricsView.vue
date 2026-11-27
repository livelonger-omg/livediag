<script setup>
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { useBlueprintStore } from '@/stores/blueprint'

const store = useBlueprintStore()

const filterPhase = ref('')
const drawerVisible = ref(false)
const isNew = ref(false)
const form = ref(null)

const kindType = { 核心: 'danger', SLA: 'primary', 效率: 'warning', 质量: 'success' }

const list = computed(() =>
  filterPhase.value ? store.metrics.filter((m) => m.phase === filterPhase.value) : store.metrics
)

const confirmedCount = computed(() => store.metrics.filter((m) => m.confirmed).length)
function humanTime(min) {
  if (min < 60) return `${min} 分钟`
  const h = min / 60
  return `${Number.isInteger(h) ? h : h.toFixed(1)} 小时`
}

const slaList = computed(() => [
  { name: '受理建单', val: humanTime(store.meta.sla.accept) },
  { name: '上门', val: humanTime(store.meta.sla.arrive) },
  { name: '结单', val: humanTime(store.meta.sla.close) }
])

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
    id: '', phase: store.phases[0].id, name: '', asIs: '', target: '',
    unit: '%', confirmed: false, definition: '', kind: '效率'
  }
  drawerVisible.value = true
}

function save() {
  if (!form.value.name.trim()) {
    ElMessage.warning('请填写指标名称')
    return
  }
  if (isNew.value) {
    store.addMetric(form.value)
    ElMessage.success('已新增指标')
  } else {
    store.updateMetric(form.value.id, form.value)
    ElMessage.success('已保存')
  }
  drawerVisible.value = false
}

function remove(row) {
  store.removeMetric(row.id)
  ElMessage.success(`已删除 ${row.id}`)
}
</script>

<template>
  <div class="mt">
    <div class="sla-row">
      <div class="sla-card">
        <div class="sla-hd">现行时限 SLA</div>
        <div class="sla-items">
          <div v-for="s in slaList" :key="s.name" class="sla-item">
            <span class="sla-name">{{ s.name }}</span>
            <span class="sla-val">{{ s.val }}</span>
          </div>
        </div>
      </div>
      <div class="sla-card">
        <div class="sla-hd">已确认口径</div>
        <div class="sla-body">
          <div><b>一次解决率</b> = 投诉一次上门解决数 ÷ 投诉总数</div>
          <div><b>重复投诉率</b> = 近 30 天投诉 &gt;1 次客户数 ÷ 近 30 天投诉客户数</div>
        </div>
      </div>
      <div class="sla-card narrow">
        <div class="sla-hd">口径确认进度</div>
        <div class="conf-val">{{ confirmedCount }} / {{ store.metrics.length }}</div>
        <el-progress
          :percentage="store.metrics.length ? Math.round((confirmedCount / store.metrics.length) * 100) : 0"
          :stroke-width="5" :show-text="false" color="var(--good)"
        />
      </div>
    </div>

    <div class="mt-bar">
      <el-select v-model="filterPhase" placeholder="全部阶段" clearable size="small" style="width: 150px">
        <el-option v-for="p in store.phases" :key="p.id" :label="p.name" :value="p.id" />
      </el-select>
      <el-button type="primary" size="small" @click="openNew">新增指标</el-button>
    </div>

    <el-table :data="list" size="small" max-height="calc(100vh - 430px)" row-key="id">
      <el-table-column label="编号" width="66">
        <template #default="{ row }"><span class="chip chip-k">{{ row.id }}</span></template>
      </el-table-column>
      <el-table-column label="阶段" width="96">
        <template #default="{ row }"><span class="ph">{{ phaseName(row.phase) }}</span></template>
      </el-table-column>
      <el-table-column prop="name" label="指标" min-width="150" />
      <el-table-column label="类型" width="86" align="center">
        <template #default="{ row }">
          <el-tag size="small" :type="kindType[row.kind] || 'info'" effect="plain">{{ row.kind }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="现状" width="110" align="center">
        <template #default="{ row }"><span class="now">{{ row.asIs }}</span></template>
      </el-table-column>
      <el-table-column label="目标" width="110" align="center">
        <template #default="{ row }"><span class="goal">{{ row.target }}</span></template>
      </el-table-column>
      <el-table-column prop="definition" label="统计口径" min-width="300" show-overflow-tooltip />
      <el-table-column label="口径" width="94" align="center">
        <template #default="{ row }">
          <el-tag v-if="row.confirmed" size="small" type="success" effect="dark">已确认</el-tag>
          <el-tag v-else size="small" type="info" effect="plain">待确认</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="118" fixed="right">
        <template #default="{ row }">
          <el-button text type="primary" size="small" @click="openEdit(row)">编辑</el-button>
          <el-popconfirm title="确认删除该指标？" width="190" @confirm="remove(row)">
            <template #reference><el-button text type="danger" size="small">删除</el-button></template>
          </el-popconfirm>
        </template>
      </el-table-column>
    </el-table>

    <el-drawer v-model="drawerVisible" :title="isNew ? '新增指标' : `编辑 ${form?.id}`" size="560px">
      <el-form v-if="form" label-position="top" size="small">
        <div class="grid2">
          <el-form-item label="编号"><el-input v-model="form.id" :disabled="!isNew" placeholder="自动生成" /></el-form-item>
          <el-form-item label="所属阶段">
            <el-select v-model="form.phase" style="width: 100%">
              <el-option v-for="p in store.phases" :key="p.id" :label="`${p.no} ${p.name}`" :value="p.id" />
            </el-select>
          </el-form-item>
        </div>
        <el-form-item label="指标名称"><el-input v-model="form.name" /></el-form-item>
        <div class="grid3">
          <el-form-item label="现状值"><el-input v-model="form.asIs" /></el-form-item>
          <el-form-item label="目标值"><el-input v-model="form.target" /></el-form-item>
          <el-form-item label="单位"><el-input v-model="form.unit" /></el-form-item>
        </div>
        <div class="grid2">
          <el-form-item label="指标类型">
            <el-select v-model="form.kind" style="width: 100%">
              <el-option label="核心" value="核心" />
              <el-option label="SLA" value="SLA" />
              <el-option label="效率" value="效率" />
              <el-option label="质量" value="质量" />
            </el-select>
          </el-form-item>
          <el-form-item label="口径是否已确认">
            <el-switch v-model="form.confirmed" active-text="已确认" inactive-text="待确认" />
          </el-form-item>
        </div>
        <el-form-item label="统计口径定义">
          <el-input v-model="form.definition" type="textarea" :autosize="{ minRows: 3 }" />
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
.mt { display: flex; flex-direction: column; gap: 12px; }
.sla-row { display: grid; grid-template-columns: 1.1fr 1.6fr .8fr; gap: 12px; }
.sla-card { background: var(--panel); border: 1px solid var(--line); border-radius: 12px; padding: 12px 16px; }
.sla-hd { font-size: 11.5px; color: var(--sub); margin-bottom: 8px; }
.sla-items { display: flex; gap: 22px; }
.sla-item { display: flex; flex-direction: column; gap: 2px; }
.sla-name { font-size: 11px; color: var(--dim); }
.sla-val { font-size: 17px; font-weight: 700; color: var(--brand); }
.sla-body { font-size: 11.8px; color: #c3ccd8; line-height: 1.85; }
.sla-body b { color: var(--text); }
.conf-val { font-size: 22px; font-weight: 700; color: #7fd6ab; margin-bottom: 6px; }
.mt-bar { display: flex; justify-content: space-between; gap: 10px; }
.ph { font-size: 12px; color: var(--brand); }
.now { color: var(--dim); }
.goal { color: #7fd6ab; font-weight: 600; }
.grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 0 14px; }
.grid3 { display: grid; grid-template-columns: repeat(3, 1fr); gap: 0 14px; }
@media (max-width: 1100px) { .sla-row { grid-template-columns: 1fr; } }
</style>
