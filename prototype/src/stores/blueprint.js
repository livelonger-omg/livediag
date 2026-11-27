import { defineStore } from 'pinia'
import { reactive } from 'vue'
import { seed, SCHEMA_VERSION } from '@/data/seed'

export const STORAGE_KEY = 'livediag.blueprint.tobe.v1'

/** 保存状态（放 store 外，避免持久化时自我触发） */
export const saveStatus = reactive({ at: '', bytes: 0, available: true })

function clone(obj) {
  return JSON.parse(JSON.stringify(obj))
}

function emptyState() {
  return clone(seed)
}

/** 从 localStorage 读取；结构不合法时回退种子数据 */
function loadState() {
  const fallback = emptyState()
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return fallback
    const data = JSON.parse(raw)
    if (!data || !Array.isArray(data.phases) || typeof data.cells !== 'object') {
      return fallback
    }
    // 版本迁移：缺失的顶层字段用种子补齐（保证老数据还能打开）
    const merged = { ...fallback, ...data }
    merged.meta = { ...fallback.meta, ...(data.meta || {}) }
    merged.ui = { ...fallback.ui, ...(data.ui || {}) }
    merged.meta.schemaVersion = SCHEMA_VERSION
    return merged
  } catch (e) {
    console.warn('[blueprint] 本地数据读取失败，已回退种子数据', e)
    saveStatus.available = false
    return fallback
  }
}

export const useBlueprintStore = defineStore('blueprint', {
  state: () => loadState(),

  getters: {
    phaseById: (state) => (id) => state.phases.find((p) => p.id === id),
    failById: (state) => (id) => state.failPoints.find((f) => f.id === id),
    cellOf: (state) => (phaseId, layerId) => state.cells[`${phaseId}.${layerId}`] || { asIs: [], toBe: [] },
    failsOfPhase: (state) => (phaseId) => state.failPoints.filter((f) => f.phase === phaseId),
    failsOfCell: (state) => (phaseId, layerId) => {
      const cell = state.cells[`${phaseId}.${layerId}`] || {}
      return (cell.f || []).map((id) => state.failPoints.find((f) => f.id === id)).filter(Boolean)
    },
    metricsOfPhase: (state) => (phaseId) => state.metrics.filter((m) => m.phase === phaseId),
    storageBytes() {
      try {
        return new Blob([JSON.stringify(this.$state)]).size
      } catch (e) {
        return 0
      }
    },
    stats: (state) => {
      const total = state.failPoints.length
      const done = state.failPoints.filter((f) => f.status === 'done').length
      const building = state.failPoints.filter((f) => f.status === 'building').length
      const planned = total - done - building
      const cellsTotal = state.phases.length * state.layers.length
      const cellsFilled = Object.values(state.cells).filter(
        (c) => (c.toBe && c.toBe.length) || (c.asIs && c.asIs.length)
      ).length
      return {
        failTotal: total,
        failDone: done,
        failBuilding: building,
        failPlanned: planned,
        failResolvedRate: total ? Math.round((done / total) * 100) : 0,
        cellsTotal,
        cellsFilled,
        cellCoverage: cellsTotal ? Math.round((cellsFilled / cellsTotal) * 100) : 0,
        buildDone: state.buildItems.filter((b) => b.status === 'done').length,
        buildTotal: state.buildItems.length
      }
    }
  },

  actions: {
    // ---------- 持久化 ----------
    persist(state) {
      try {
        const payload = JSON.stringify(state)
        localStorage.setItem(STORAGE_KEY, payload)
        saveStatus.at = new Date().toLocaleTimeString('zh-CN', { hour12: false })
        saveStatus.bytes = new Blob([payload]).size
        saveStatus.available = true
      } catch (e) {
        saveStatus.available = false
        console.warn('[blueprint] 写入本地存储失败', e)
      }
    },

    saveNow() {
      this.persist(this.$state)
    },

    resetAll() {
      this.$patch(emptyState())
      this.saveNow()
    },

    exportJSON() {
      const data = {
        exportedAt: new Date().toISOString(),
        ...this.$state
      }
      return JSON.stringify(data, null, 2)
    },

    importJSON(text) {
      const data = JSON.parse(text)
      if (!data || !Array.isArray(data.phases) || typeof data.cells !== 'object') {
        throw new Error('文件结构不匹配：缺少 phases 或 cells 字段')
      }
      if (!Array.isArray(data.failPoints) || !Array.isArray(data.layers)) {
        throw new Error('文件结构不匹配：缺少 layers 或 failPoints 字段')
      }
      const fallback = emptyState()
      const merged = { ...fallback, ...data }
      merged.meta = { ...fallback.meta, ...(data.meta || {}), schemaVersion: SCHEMA_VERSION }
      merged.ui = { ...fallback.ui, ...(data.ui || {}) }
      this.$patch(merged)
      this.saveNow()
      return data
    },

    // ---------- 蓝图单元格 ----------
    setCellItem(phaseId, layerId, field, index, value) {
      const key = `${phaseId}.${layerId}`
      if (!this.cells[key]) this.cells[key] = { asIs: [], toBe: [] }
      if (!Array.isArray(this.cells[key][field])) this.cells[key][field] = []
      this.cells[key][field][index] = value
    },

    addCellItem(phaseId, layerId, field, value = '') {
      const key = `${phaseId}.${layerId}`
      if (!this.cells[key]) this.cells[key] = { asIs: [], toBe: [] }
      if (!Array.isArray(this.cells[key][field])) this.cells[key][field] = []
      this.cells[key][field].push(value)
    },

    removeCellItem(phaseId, layerId, field, index) {
      const key = `${phaseId}.${layerId}`
      const cell = this.cells[key]
      if (!cell || !Array.isArray(cell[field])) return
      cell[field].splice(index, 1)
    },

    setCellTags(phaseId, layerId, field, ids) {
      const key = `${phaseId}.${layerId}`
      if (!this.cells[key]) this.cells[key] = { asIs: [], toBe: [] }
      this.cells[key][field] = Array.isArray(ids) ? [...ids] : []
    },

    // ---------- 失效点 ----------
    updateFailPoint(id, patch) {
      const target = this.failPoints.find((f) => f.id === id)
      if (target) Object.assign(target, patch)
    },

    addFailPoint(payload) {
      const maxNo = this.failPoints.reduce((max, f) => {
        const n = parseInt(String(f.id).replace(/\D/g, ''), 10)
        return Number.isFinite(n) ? Math.max(max, n) : max
      }, 0)
      const item = {
        id: `F${maxNo + 1}`,
        phase: payload.phase || this.phases[0].id,
        name: payload.name || '未命名失效点',
        consequence: payload.consequence || '',
        cause: payload.cause || '',
        auto: payload.auto || '中',
        status: payload.status || 'planned',
        solution: payload.solution || '',
        acceptance: payload.acceptance || '',
        owner: payload.owner || ''
      }
      this.failPoints.push(item)
      return item
    },

    removeFailPoint(id) {
      const idx = this.failPoints.findIndex((f) => f.id === id)
      if (idx >= 0) this.failPoints.splice(idx, 1)
      Object.values(this.cells).forEach((cell) => {
        if (Array.isArray(cell.f)) {
          cell.f = cell.f.filter((x) => x !== id)
        }
      })
      this.buildItems.forEach((b) => {
        if (Array.isArray(b.fails)) b.fails = b.fails.filter((x) => x !== id)
      })
    },

    // ---------- 指标 ----------
    updateMetric(id, patch) {
      const target = this.metrics.find((m) => m.id === id)
      if (target) Object.assign(target, patch)
    },

    addMetric(payload) {
      const maxNo = this.metrics.reduce((max, m) => {
        const n = parseInt(String(m.id).replace(/\D/g, ''), 10)
        return Number.isFinite(n) ? Math.max(max, n) : max
      }, 0)
      const item = {
        id: `K${maxNo + 1}`,
        phase: payload.phase || this.phases[0].id,
        name: payload.name || '未命名指标',
        asIs: payload.asIs || '',
        target: payload.target || '',
        unit: payload.unit || '%',
        confirmed: payload.confirmed !== false,
        definition: payload.definition || '',
        kind: payload.kind || '效率'
      }
      this.metrics.push(item)
      return item
    },

    removeMetric(id) {
      const idx = this.metrics.findIndex((m) => m.id === id)
      if (idx >= 0) this.metrics.splice(idx, 1)
    },

    // ---------- 差距 / 建设内容 ----------
    updateGap(id, patch) {
      const target = this.gaps.find((g) => g.id === id)
      if (target) Object.assign(target, patch)
    },

    updateBuildItem(id, patch) {
      const target = this.buildItems.find((b) => b.id === id)
      if (target) Object.assign(target, patch)
    },

    setViewMode(mode) {
      this.ui.viewMode = mode
    }
  }
})
