import { ref } from 'vue'
import { defineStore } from 'pinia'
import { apiFetch } from '@/lib/api'

export const useApprovalsStore = defineStore('approvals', () => {
  const pendingCount = ref(0)

  async function refresh() {
    try {
      const [ar, br] = await Promise.all([
        apiFetch('/articles?status=in-review'),
        apiFetch('/blog?status=in-review'),
      ])
      if (!ar.ok || !br.ok) return
      const [a, b]: [unknown[], unknown[]] = await Promise.all([ar.json(), br.json()])
      pendingCount.value = a.length + b.length
    } catch {
      // non-fatal — badge just stays at last known value
    }
  }

  return { pendingCount, refresh }
})
