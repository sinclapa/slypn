import { useAuthStore } from '@/stores/auth'
import { useEditorQueueStore } from '@/stores/editorQueue'
import { useApprovalsStore } from '@/stores/approvals'

/**
 * The two nav badges — Editor and Approvals — are derived from server state, and AppNav only
 * fetches them when it mounts. Anything that moves a document between drafts, review and
 * published therefore has to say so, or the numbers in the account menu keep describing the
 * moment the page loaded.
 *
 * Both are refreshed together on purpose. Deciding per call site which of the two a given
 * transition moves has been wrong three times now, and wrong in the non-obvious direction:
 * submitting and withdrawing leave the Editor count unchanged — the document just moves
 * between drafts and review, and it counts both — while moving the Approvals count by one.
 * Editing a published item is the mirror image: it mints a revision draft, so the Editor
 * count moves and Approvals does not.
 *
 * Call this after the action has succeeded, never before. A stale count is wrong, but a count
 * refreshed away from a document that is still there tells the user a failed action worked.
 *
 * Approvals is fetched only for an Admin, who is the only one with that badge.
 */
export function useContentBadges() {
  const auth        = useAuthStore()
  const editorQueue = useEditorQueueStore()
  const approvals   = useApprovalsStore()

  function refreshContentBadges() {
    editorQueue.refresh()
    if (auth.isAdmin) approvals.refresh()
  }

  return { refreshContentBadges }
}
