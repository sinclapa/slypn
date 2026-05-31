import { onMounted, ref, type Ref } from 'vue'

export interface AsyncDataOptions {
  /** If true, you have to call refresh() yourself; otherwise it runs onMounted. */
  lazy?: boolean
}

export function useAsyncData<T>(
  fetcher: () => Promise<T>,
  options: AsyncDataOptions = {},
) {
  const data = ref<T | null>(null) as Ref<T | null>
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function refresh() {
    loading.value = true
    error.value = null
    try {
      data.value = await fetcher()
    } catch (err) {
      error.value = err instanceof Error ? err.message : String(err)
    } finally {
      loading.value = false
    }
  }

  if (!options.lazy) onMounted(refresh)
  return { data, loading, error, refresh }
}
