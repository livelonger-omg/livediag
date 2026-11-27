<script setup>
import { ref, computed } from 'vue'
import { useBlueprintStore, saveStatus } from '@/stores/blueprint'
import BoardView from '@/views/BoardView.vue'
import FailPointsView from '@/views/FailPointsView.vue'
import MetricsView from '@/views/MetricsView.vue'
import GapsView from '@/views/GapsView.vue'
import DataView from '@/views/DataView.vue'

const store = useBlueprintStore()
const activeTab = ref(store.ui?.activeTab || 'board')

const tabs = [
  { name: 'board', label: '蓝图看板', icon: 'Grid' },
  { name: 'fails', label: '失效点', icon: 'Warning' },
  { name: 'metrics', label: '指标与 SLA', icon: 'TrendCharts' },
  { name: 'gaps', label: '差距与建设', icon: 'Connection' },
  { name: 'data', label: '数据管理', icon: 'FolderOpened' }
]

const saveText = computed(() => {
  if (!saveStatus.available) return '本地存储不可用'
  return saveStatus.at ? `已自动保存 ${saveStatus.at}` : '尚未修改'
})

const storageText = computed(() => {
  const kb = saveStatus.bytes / 1024
  return kb > 1024 ? `${(kb / 1024).toFixed(2)} MB` : `${kb.toFixed(1)} KB`
})

function onTabChange(name) {
  store.ui.activeTab = name
}
</script>

<template>
  <div class="app-shell">
    <header class="app-header">
      <div class="brand-block">
        <div class="brand-mark">LD</div>
        <div class="brand-text">
          <div class="kicker">SERVICE BLUEPRINT · TO-BE PROTOTYPE</div>
          <h1>{{ store.meta.title }}</h1>
          <div class="sub">{{ store.meta.subtitle }} ｜ {{ store.meta.version }}</div>
        </div>
      </div>

      <div class="header-actions">
        <el-tooltip content="所有修改自动写入浏览器 localStorage，不上传任何数据" placement="bottom">
          <el-tag size="small" effect="dark" type="success" round>
            <span class="status-dot done" />{{ saveText }}
          </el-tag>
        </el-tooltip>
        <el-tag size="small" effect="plain" round>本地占用 {{ storageText }}</el-tag>
        <el-button size="small" @click="activeTab = 'data'; onTabChange('data')">数据管理</el-button>
      </div>
    </header>

    <main class="main-area">
      <el-tabs v-model="activeTab" @tab-change="onTabChange">
        <el-tab-pane v-for="t in tabs" :key="t.name" :name="t.name" :label="t.label">
          <BoardView v-if="t.name === 'board'" @open-tab="(n) => { activeTab = n; onTabChange(n) }" />
          <FailPointsView v-else-if="t.name === 'fails'" />
          <MetricsView v-else-if="t.name === 'metrics'" />
          <GapsView v-else-if="t.name === 'gaps'" />
          <DataView v-else-if="t.name === 'data'" />
        </el-tab-pane>
      </el-tabs>
    </main>
  </div>
</template>
