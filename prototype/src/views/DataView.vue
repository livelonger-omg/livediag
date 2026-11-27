<script setup>
import { ref, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useBlueprintStore, saveStatus, STORAGE_KEY } from '@/stores/blueprint'

const store = useBlueprintStore()
const fileInput = ref(null)
const pasteText = ref('')
const importMode = ref('file')

const bytesText = computed(() => {
  const kb = store.storageBytes / 1024
  return kb > 1024 ? `${(kb / 1024).toFixed(2)} MB` : `${kb.toFixed(1)} KB`
})

const uploadPercent = computed(() => {
  const limit = 5 * 1024 * 1024
  return Math.min(100, Math.round((store.storageBytes / limit) * 100))
})

function download(name, content, type = 'application/json') {
  const blob = new Blob([content], { type: `${type};charset=utf-8` })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = name
  a.click()
  URL.revokeObjectURL(url)
}

function exportJSON() {
  const stamp = new Date().toISOString().slice(0, 10)
  download(`livediag-blueprint-tobe-${stamp}.json`, store.exportJSON())
  ElMessage.success('已导出 JSON 快照')
}

function exportMarkdown() {
  const md = buildMarkdown()
  download(`livediag-blueprint-tobe-${new Date().toISOString().slice(0, 10)}.md`, md, 'text/markdown')
  ElMessage.success('已导出 Markdown 摘要')
}

function buildMarkdown() {
  const lines = []
  lines.push(`# ${store.meta.title}`, '')
  lines.push(`- 版本：${store.meta.version}`)
  lines.push(`- 单位：${store.meta.subtitle}`)
  lines.push(`- 日期：${store.meta.asOf}`)
  lines.push(`- 范围：${store.meta.scope}`)
  lines.push(`- 现行 SLA：受理 ${store.meta.sla.accept}min / 上门 ${store.meta.sla.arrive}min / 结单 ${store.meta.sla.close / 60}h`, '')

  lines.push('## 一、阶段与目标', '', '| 阶段 | SLA | 责任方 | 目标 |', '| --- | --- | --- | --- |')
  store.phases.forEach((p) => lines.push(`| ${p.no} ${p.name} | ${p.sla} | ${p.owner} | ${p.goal} |`))
  lines.push('')

  lines.push('## 二、失效点清单（现状 → 目标态）', '', '| 编号 | 阶段 | 失效点 | 现状根因 | 目标态 | 状态 | 验收口径 |', '| --- | --- | --- | --- | --- | --- | --- |')
  store.failPoints.forEach((f) => {
    const ph = store.phases.find((p) => p.id === f.phase)?.name || ''
    const st = { done: '已消除', building: '建设中', planned: '待建设' }[f.status] || f.status
    lines.push(`| ${f.id} | ${ph} | ${f.name} | ${f.cause} | ${f.solution} | ${st} | ${f.acceptance} |`)
  })
  lines.push('')

  lines.push('## 三、指标与 SLA', '', '| 编号 | 阶段 | 指标 | 现状 | 目标 | 口径 | 已确认 |', '| --- | --- | --- | --- | --- | --- | --- |')
  store.metrics.forEach((m) => {
    const ph = store.phases.find((p) => p.id === m.phase)?.name || ''
    lines.push(`| ${m.id} | ${ph} | ${m.name} | ${m.asIs} | ${m.target} | ${m.definition} | ${m.confirmed ? '是' : '否'} |`)
  })
  lines.push('')

  lines.push('## 四、服务差距', '')
  store.gaps.forEach((g) => {
    lines.push(`### ${g.name}`, `- 定义：${g.definition}`, `- 现状：${g.asIs}`, `- 证据：${g.evidence}`, `- 手段：${g.action}`, `- 目标：${g.target}`, '')
  })

  lines.push('## 五、建设内容', '', '| 编号 | 环节 | 失效点 | 建设内容 | 验收口径 | 状态 |', '| --- | --- | --- | --- | --- | --- |')
  store.buildItems.forEach((b) => {
    const ph = store.phases.find((p) => p.id === b.phase)?.name || ''
    const st = { done: '已实现', building: '建设中', planned: '待建设' }[b.status] || b.status
    lines.push(`| ${b.id} | ${ph} | ${(b.fails || []).join(' / ')} | ${b.content} | ${b.acceptance} | ${st} |`)
  })
  return lines.join('\n')
}

function triggerImport() {
  fileInput.value?.click()
}

function onFile(e) {
  const file = e.target.files?.[0]
  if (!file) return
  const reader = new FileReader()
  reader.onload = () => {
    try {
      store.importJSON(String(reader.result))
      ElMessage.success('导入成功，已覆盖当前数据')
    } catch (err) {
      ElMessage.error(`导入失败：${err.message}`)
    }
  }
  reader.readAsText(file, 'utf-8')
  e.target.value = ''
}

function importFromText() {
  if (!pasteText.value.trim()) {
    ElMessage.warning('请先粘贴 JSON 内容')
    return
  }
  try {
    store.importJSON(pasteText.value)
    pasteText.value = ''
    ElMessage.success('导入成功，已覆盖当前数据')
  } catch (err) {
    ElMessage.error(`导入失败：${err.message}`)
  }
}

async function reset() {
  try {
    await ElMessageBox.confirm('将清空本地修改并恢复到内置的 To-Be 种子数据，是否继续？', '重置数据', {
      type: 'warning',
      confirmButtonText: '确认重置',
      cancelButtonText: '取消'
    })
    store.resetAll()
    ElMessage.success('已恢复到种子数据')
  } catch (e) {
    /* 用户取消 */
  }
}

async function clearStorage() {
  try {
    await ElMessageBox.confirm('仅清空浏览器本地缓存，页面刷新后回到种子数据。', '清空本地存储', {
      type: 'warning',
      confirmButtonText: '清空',
      cancelButtonText: '取消'
    })
    localStorage.removeItem(STORAGE_KEY)
    ElMessage.success('已清空，刷新页面生效')
  } catch (e) {
    /* 用户取消 */
  }
}
</script>

<template>
  <div class="dt">
    <div class="dt-grid">
      <div class="card">
        <div class="card-hd">本地存储</div>
        <div class="kv-line"><span>存储键</span><b class="mono">{{ STORAGE_KEY }}</b></div>
        <div class="kv-line"><span>占用空间</span><b>{{ bytesText }}</b></div>
        <div class="kv-line"><span>最近保存</span><b>{{ saveStatus.at || '—' }}</b></div>
        <div class="kv-line"><span>存储可用</span><b :class="saveStatus.available ? 'ok' : 'bad'">{{ saveStatus.available ? '正常' : '不可用' }}</b></div>
        <el-progress :percentage="uploadPercent" :stroke-width="5" :show-text="false" color="var(--brand)" style="margin: 10px 0 4px" />
        <div class="tiny">浏览器 localStorage 常见上限约 5MB，当前数据远低于阈值</div>
      </div>

      <div class="card">
        <div class="card-hd">数据规模</div>
        <div class="kv-line"><span>阶段 / 泳道</span><b>{{ store.phases.length }} / {{ store.layers.length }}</b></div>
        <div class="kv-line"><span>蓝图单元格</span><b>{{ Object.keys(store.cells).length }}</b></div>
        <div class="kv-line"><span>失效点 / 等待点</span><b>{{ store.failPoints.length }} / {{ store.waits.length }}</b></div>
        <div class="kv-line"><span>指标 / 差距 / 建设</span><b>{{ store.metrics.length }} / {{ store.gaps.length }} / {{ store.buildItems.length }}</b></div>
        <div class="tiny">所有数据均在本机浏览器内，不上传服务器</div>
      </div>

      <div class="card">
        <div class="card-hd">导出</div>
        <p class="card-desc">导出 JSON 可用于备份或在其他机器导入；Markdown 可直接粘进立项文档。</p>
        <div class="btn-col">
          <el-button type="primary" size="small" @click="exportJSON">导出 JSON 快照</el-button>
          <el-button size="small" @click="exportMarkdown">导出 Markdown 摘要</el-button>
        </div>
      </div>

      <div class="card">
        <div class="card-hd">导入</div>
        <el-radio-group v-model="importMode" size="small" style="margin-bottom: 10px">
          <el-radio-button value="file">选择文件</el-radio-button>
          <el-radio-button value="text">粘贴 JSON</el-radio-button>
        </el-radio-group>
        <div v-if="importMode === 'file'">
          <p class="card-desc">选择此前导出的 JSON 文件，导入后覆盖当前数据。</p>
          <input ref="fileInput" type="file" accept="application/json,.json" class="hidden-file" @change="onFile" />
          <el-button size="small" @click="triggerImport">选择 JSON 文件</el-button>
        </div>
        <div v-else>
          <el-input v-model="pasteText" type="textarea" :rows="4" placeholder="粘贴导出的 JSON 内容" />
          <el-button size="small" type="primary" style="margin-top: 8px" @click="importFromText">导入</el-button>
        </div>
      </div>
    </div>

    <div class="card danger-card">
      <div class="card-hd">危险操作</div>
      <div class="danger-row">
        <div>
          <b>恢复种子数据</b>
          <p class="card-desc">丢弃所有本地修改，回到内置的 To-Be 蓝图。</p>
        </div>
        <el-button size="small" type="warning" plain @click="reset">重置数据</el-button>
      </div>
      <div class="danger-row">
        <div>
          <b>清空本地缓存</b>
          <p class="card-desc">删除 localStorage 记录，刷新后重新载入种子。</p>
        </div>
        <el-button size="small" type="danger" plain @click="clearStorage">清空存储</el-button>
      </div>
    </div>

    <div class="card">
      <div class="card-hd">使用说明</div>
      <ul class="tips">
        <li>所有编辑即时生效并自动写入 localStorage，无需手动保存；刷新页面数据仍在。</li>
        <li>蓝图看板中点击任意单元格可编辑“现状 / 目标态”条目，F / W 徽标表示该格关联的失效点与等待点。</li>
        <li>指标口径中标注“已确认”的为一线确认口径（一次解决率、重复投诉率、三项时限 SLA），其余为待核实的估算值。</li>
        <li>清空浏览器数据或更换浏览器会丢失本地修改，重要版本请先用“导出 JSON”备份。</li>
      </ul>
    </div>
  </div>
</template>

<style scoped>
.dt { display: flex; flex-direction: column; gap: 12px; }
.dt-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 12px; }
.card { background: var(--panel); border: 1px solid var(--line); border-radius: 12px; padding: 14px 18px; }
.card-hd { font-size: 13px; font-weight: 600; margin-bottom: 10px; }
.card-desc { font-size: 11.8px; color: var(--sub); line-height: 1.75; margin: 0 0 10px; }
.kv-line { display: flex; justify-content: space-between; gap: 10px; font-size: 12px; color: var(--sub); padding: 4px 0; border-bottom: 1px dashed rgba(36, 48, 64, .7); }
.kv-line b { color: var(--text); font-weight: 600; word-break: break-all; text-align: right; }
.kv-line b.ok { color: #7fd6ab; }
.kv-line b.bad { color: #ff9e86; }
.tiny { font-size: 10.5px; color: var(--dim); margin-top: 8px; line-height: 1.7; }
.btn-col { display: flex; flex-direction: column; gap: 8px; align-items: flex-start; }
.hidden-file { display: none; }
.danger-card { border-color: rgba(226, 96, 60, .3); }
.danger-row { display: flex; align-items: center; justify-content: space-between; gap: 14px; padding: 8px 0; border-bottom: 1px dashed rgba(36, 48, 64, .7); }
.danger-row:last-child { border-bottom: none; }
.danger-row b { font-size: 12.5px; }
.tips { margin: 0; padding-left: 18px; font-size: 12px; color: var(--sub); line-height: 1.95; }
</style>
