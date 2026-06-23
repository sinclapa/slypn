import { ref } from 'vue'
import { defineStore } from 'pinia'
import { apiFetch } from '@/lib/api'

export const useApprovalsStore = defineStore('approvals', () => {
  const pendingCount = ref(0)

  async function refresh() {
    try {
      const [ar, br, pa, pb] = await Promise.all([
        apiFetch('/articles?status=in-review'),
        apiFetch('/blog?status=in-review'),
        apiFetch('/articles?status=published'),
        apiFetch('/blog?status=published'),
      ])
      if (!ar.ok || !br.ok) return
      const [a, b]: [unknown[], unknown[]] = await Promise.all([ar.json(), br.json()])
      let count = a.length + b.length

      // Pending deletion requests also need admin action.
      if (pa.ok && pb.ok) {
        const [pubA, pubB] = await Promise.all([
          pa.json() as Promise<{ deletionRequestedBy?: string | null }[]>,
          pb.json() as Promise<{ deletionRequestedBy?: string | null }[]>,
        ])
        count += [...pubA, ...pubB].filter(x => x.deletionRequestedBy).length
      }
      pendingCount.value = count
    } catch {
      // non-fatal — badge just stays at last known value
    }
  }

  return { pendingCount, refresh }
})
