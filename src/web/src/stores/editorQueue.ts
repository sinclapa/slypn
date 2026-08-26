import { ref } from 'vue'
import { defineStore } from 'pinia'
import { apiFetch } from '@/lib/api'
import { useAuthStore } from '@/stores/auth'

/**
 * How many documents the signed-in user has on the go in the editor: open drafts
 * plus their own submissions awaiting review. Drives the badge on the Editor nav
 * link, so pending work is visible without opening the page.
 *
 * Own submissions, not all of them: the API filters /review/* to the caller for a
 * Contributor, but an Admin legitimately sees everyone's, and counting those here
 * would tell an admin they have twelve documents open when they have none.
 */
export const useEditorQueueStore = defineStore('editorQueue', () => {
  const draftCount    = ref(0)
  const inReviewCount = ref(0)
  const openCount     = ref(0)

  async function refresh() {
    const auth = useAuthStore()
    if (!auth.isAuthenticated) {
      draftCount.value = inReviewCount.value = openCount.value = 0
      return
    }
    try {
      const [dr, ar, br] = await Promise.all([
        apiFetch('/drafts'),
        apiFetch('/review/articles'),
        apiFetch('/review/blog'),
      ])
      if (!dr.ok || !ar.ok || !br.ok) return

      const [drafts, articles, blogs] = await Promise.all([
        dr.json() as Promise<unknown[]>,
        ar.json() as Promise<{ authorId?: string | null }[]>,
        br.json() as Promise<{ authorId?: string | null }[]>,
      ])

      draftCount.value = drafts.length
      inReviewCount.value = [...articles, ...blogs]
        .filter(x => auth.oid && x.authorId === auth.oid).length
      openCount.value = draftCount.value + inReviewCount.value
    } catch {
      // non-fatal — the badge just stays at its last known value
    }
  }

  return { draftCount, inReviewCount, openCount, refresh }
})
