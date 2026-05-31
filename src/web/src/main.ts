import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import { useAuthStore } from '@/stores/auth'
import { setupFaro } from '@/lib/faro'
import './assets/main.css'

const app = createApp(App)
app.use(createPinia())
app.use(router)

// Faro is consent-gated — it watches the cookie banner state and only
// initialises once the user has accepted. Safe to wire up unconditionally.
setupFaro()

// Drive MSAL through initialize + handleRedirectPromise once before
// the first render so the nav can show the signed-in account immediately.
const auth = useAuthStore()
auth.initialize().finally(() => app.mount('#app'))
