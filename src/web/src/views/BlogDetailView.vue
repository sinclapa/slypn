<script setup lang="ts">
import { computed, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { apiFetch } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { Article } from '@/types/content'
import EditContentButton from '@/components/common/EditContentButton.vue'

const route = useRoute()
const router = useRouter()
const slug = computed(() => String(route.params.slug ?? ''))

// When we arrived from the blog list, go back so the kept-alive list view restores
// its scroll position; otherwise navigate to it directly.
function backToBlog() {
  const back = router.options.history.state.back
  if (typeof back === 'string' && back.split('?')[0].split('#')[0] === '/blog') router.back()
  else router.push('/blog')
}

const { data: post, loading, error, refresh } = useAsyncData(async () => {
  const resp = await apiFetch(`/blog/${encodeURIComponent(slug.value)}`)
  if (resp.status === 404) return null
  if (!resp.ok) throw new Error(`${resp.status} ${resp.statusText}`)
  return resp.json() as Promise<Article>
})

watch(slug, refresh)

const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' })
</script>

<template>
  <article v-if="post" data-testid="blog-detail" class="page-container-prose py-16">
    <button type="button" data-testid="blog-back" class="text-sm text-slypn-600 hover:text-slypn-700" @click="backToBlog">
      &larr; All posts
    </button>
    <h1 class="mt-6 text-4xl font-extrabold text-slypn-700 sm:text-5xl">
      {{ post.title }}
    </h1>
    <p class="mt-4 flex items-center gap-3 text-slypn-900/65">
      <span>{{ post.author }} &middot; {{ formatDate(post.publishedAt) }}</span>
      <EditContentButton
        :content-id="post.id"
        :can-edit="post.canEdit"
        label="Edit this post"
        @submitted="refresh"
      />
    </p>
    <p v-if="post.summary" class="mt-6 text-xl text-slypn-900/85">{{ post.summary }}</p>

    <div class="prose prose-slypn mt-8 max-w-none text-slypn-900/85" v-html="post.body" />

    <nav
      v-if="post.prev || post.next"
      class="mt-16 grid grid-cols-2 gap-4 border-t border-slypn-100 pt-8"
      aria-label="Blog navigation"
    >
      <RouterLink
        v-if="post.prev"
        :to="`/blog/${post.prev.slug}`"
        class="group rounded-xl border border-slypn-100 p-5 transition hover:border-slypn-300 hover:shadow-sm"
      >
        <p class="text-xs font-semibold uppercase tracking-wider text-slypn-400 group-hover:text-slypn-600">
          &larr; Previous
        </p>
        <p class="mt-2 text-sm font-medium text-slypn-700 line-clamp-2 group-hover:text-slypn-900">
          {{ post.prev.title }}
        </p>
      </RouterLink>
      <div v-else />

      <RouterLink
        v-if="post.next"
        :to="`/blog/${post.next.slug}`"
        class="group rounded-xl border border-slypn-100 p-5 text-right transition hover:border-slypn-300 hover:shadow-sm"
      >
        <p class="text-xs font-semibold uppercase tracking-wider text-slypn-400 group-hover:text-slypn-600">
          Next &rarr;
        </p>
        <p class="mt-2 text-sm font-medium text-slypn-700 line-clamp-2 group-hover:text-slypn-900">
          {{ post.next.title }}
        </p>
      </RouterLink>
      <div v-else />
    </nav>
  </article>

  <section v-else-if="loading" class="page-container-prose py-20 text-center">
    <p class="text-slypn-900/70">Loading&hellip;</p>
  </section>

  <section v-else-if="error" class="page-container-prose py-20 text-center">
    <h1 class="font-display text-2xl font-bold text-slypn-700">Couldn&rsquo;t load this post</h1>
    <p class="mt-3 text-sm text-rose-700">{{ error }}</p>
    <RouterLink to="/blog" class="mt-6 inline-block text-slypn-600 underline underline-offset-4 hover:text-slypn-700">
      Back to blog
    </RouterLink>
  </section>

  <section v-else class="page-container-prose py-20 text-center">
    <h1 class="font-display text-2xl font-bold text-slypn-700">Post not found</h1>
    <RouterLink to="/blog" class="mt-6 inline-block text-slypn-600 underline underline-offset-4 hover:text-slypn-700">
      Back to blog
    </RouterLink>
  </section>
</template>
