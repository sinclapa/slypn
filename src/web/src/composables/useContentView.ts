import { watch, type Ref } from 'vue'
import { getFaro } from '@/lib/faro'

/**
 * The fields a tracked item may carry. Every one is optional because the four content types
 * do not share a shape — a newsletter has no slug, an event has no category — and a missing
 * dimension should narrow the reporting rather than drop the event.
 */
export interface TrackedContent {
  id?: string
  slug?: string
  title?: string
  type?: string
  category?: string
}

/**
 * Report *which* piece of content is on screen, not just that a detail page was opened.
 *
 * Faro's own view tracking is keyed on the route name, so every article arrives as
 * `view_name=article-detail`. The identity is technically recoverable from `page_url`, but
 * only by parsing a slug out of a URL — and prod currently serves from more than one
 * hostname, so the same article splits across several `page_url` values and has to be
 * stitched back together in every query. This emits the slug and title as their own
 * dimensions instead, which is both cheaper to query and stable across hostnames.
 *
 * Keyed on the item rather than on mount, deliberately. The detail views refresh in place
 * when the prev/next links change the route param, so the component is not re-created and a
 * mount-time hook would miss every step through a series.
 *
 * Inert without telemetry consent: getFaro() returns null until the visitor accepts the
 * cookie banner, so this reports nothing and costs nothing until then.
 */
export function useContentView(
  item: Ref<TrackedContent | null | undefined>,
  kind: 'article' | 'blog' | 'newsletter' | 'event',
) {
  // One event per distinct item. Without this, any re-fetch of the same content — a manual
  // refresh, a retry after an error — would report a second view nobody made.
  let lastReported = ''

  watch(item, (value) => {
    if (!value) return
    const key = value.slug || value.id
    if (!key || key === lastReported) return
    lastReported = key

    getFaro()?.api.pushEvent('content_viewed', {
      // The item's own type where it has one: a blog post is an article row with
      // type="blog", so trusting the calling view would mislabel a mirrored item.
      content_kind:     value.type || kind,
      content_slug:     key,
      content_title:    value.title ?? '',
      content_category: value.category ?? '',
    })
  }, { immediate: true })
}
