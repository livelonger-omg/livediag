import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'

import 'element-plus/dist/index.css'
import 'element-plus/theme-chalk/dark/css-vars.css'
import './styles.css'

import App from './App.vue'
import { useBlueprintStore } from './stores/blueprint'

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(ElementPlus, { locale: zhCn })

for (const [key, component] of Object.entries(ElementPlusIconsVue)) {
  app.component(key, component)
}

document.documentElement.classList.add('dark')

const store = useBlueprintStore()
// 任意 state 变化自动落盘，保证“纯前端 + 本地持久化”的可用性
store.$subscribe((_mutation, state) => {
  store.persist(state)
}, { detached: true })

app.mount('#app')
