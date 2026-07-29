<script setup lang="ts">
import { computed, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { apiFetch } from '@/lib/api'
import { useAsyncData } from '@/composables/useAsyncData'
import type { Article } from '@/types/content'

const route = useRoute()
const router = useRouter()
const slug = computed(() => String(route.params.slug ?? ''))

// When we arrived here from the articles list, go back so the kept-alive list
// view restores its filter + scroll position; otherwise navigate to it directly.
function backToArticles() {
  const back = router.options.history.state.back
  if (typeof back === 'string' && back.split('?')[0] === '/articles') router.back()
  else router.push('/articles')
}

const { data: article, loading, error, refresh } = useAsyncData(async () => {
  const resp = await apiFetch(`/articles/${encodeURIComponent(slug.value)}`)
  if (resp.status === 404) return null
  if (!resp.ok) throw new Error(`${resp.status} ${resp.statusText}`)
  return resp.json() as Promise<Article>
})

watch(slug, refresh)

const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' })
</script>

<template>
  <article v-if="article" class="page-container-prose py-16">
    <button type="button" class="text-sm text-slypn-600 hover:text-slypn-700" @click="backToArticles">
      &larr; All articles
    </button>
    <p class="mt-6 font-display text-xs font-semibold uppercase tracking-widest text-slypn-500">
      {{ article.category }}
    </p>
    <h1 class="mt-2 text-4xl font-extrabold text-slypn-700 sm:text-5xl">
      {{ article.title }}
    </h1>
    <p class="mt-4 text-slypn-900/65">
      {{ article.author }} &middot; {{ formatDate(article.publishedAt) }} &middot; {{ article.readingMinutes }} min read
    </p>
    <p class="mt-6 text-xl text-slypn-900/85">{{ article.summary }}</p>

    <div class="prose prose-slypn mt-8 max-w-none text-slypn-900/85" v-html="article.body" />

    <nav
      v-if="article.prev || article.next"
      class="mt-16 grid grid-cols-2 gap-4 border-t border-slypn-100 pt-8"
      aria-label="Article navigation"
    >
      <RouterLink
        v-if="article.prev"
        :to="`/articles/${article.prev.slug}`"
        class="group rounded-xl border border-slypn-100 p-5 transition hover:border-slypn-300 hover:shadow-sm"
      >
        <p class="text-xs font-semibold uppercase tracking-wider text-slypn-400 group-hover:text-slypn-600">
          &larr; Previous
        </p>
        <p class="mt-2 text-sm font-medium text-slypn-700 line-clamp-2 group-hover:text-slypn-900">
          {{ article.prev.title }}
        </p>
      </RouterLink>
      <div v-else />

      <RouterLink
        v-if="article.next"
        :to="`/articles/${article.next.slug}`"
        class="group rounded-xl border border-slypn-100 p-5 text-right transition hover:border-slypn-300 hover:shadow-sm"
      >
        <p class="text-xs font-semibold uppercase tracking-wider text-slypn-400 group-hover:text-slypn-600">
          Next &rarr;
        </p>
        <p class="mt-2 text-sm font-medium text-slypn-700 line-clamp-2 group-hover:text-slypn-900">
          {{ article.next.title }}
        </p>
      </RouterLink>
      <div v-else />
    </nav>
  </article>

  <section v-else-if="loading" class="page-container-prose py-20 text-center">
    <p class="text-slypn-900/70">Loading&hellip;</p>
  </section>

  <section v-else-if="error" class="page-container-prose py-20 text-center">
    <h1 class="font-display text-2xl font-bold text-slypn-700">Couldn&rsquo;t load this article</h1>
    <p class="mt-3 text-sm text-rose-700">{{ error }}</p>
    <RouterLink to="/articles" class="mt-6 inline-block text-slypn-600 underline underline-offset-4 hover:text-slypn-700">
      Back to articles
    </RouterLink>
  </section>

  <section v-else class="page-container-prose py-20 text-center">
    <h1 class="font-display text-3xl font-bold text-slypn-700">Article not found</h1>
    <RouterLink to="/articles" class="mt-6 inline-block text-slypn-600 underline underline-offset-4 hover:text-slypn-700">
      Back to articles
    </RouterLink>
  </section>
</template>
