import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import { useAuthStore } from '@/stores/auth'
import './assets/main.css'

const app = createApp(App)
app.use(createPinia())
app.use(router)

// Drive MSAL through initialize + handleRedirectPromise once before
// the first render so the nav can show the signed-in account immediately.
const auth = useAuthStore()
auth.initialize().finally(() => app.mount('#app'))
