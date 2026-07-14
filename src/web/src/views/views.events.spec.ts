import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>()
  return { ...actual, apiJson, apiFetch }
})
const route = { params: {} as Record<string, string>, query: {} as Record<string, string> }
const router = { push: vi.fn(), replace: vi.fn().mockResolvedValue(undefined), back: vi.fn() }
vi.mock('vue-router', async (orig) => {
  const actual = await (orig() as Promise<Record<string, unknown>>)
  return { ...actual, useRoute: () => route, useRouter: () => router }
})

import MonthRangePicker from '@/components/common/MonthRangePicker.vue'
import EventFormDialog from '@/components/common/EventFormDialog.vue'
import EventCalendar from '@/components/common/EventCalendar.vue'
import EventManagementView from './EventManagementView.vue'
import { useAuthStore } from '@/stores/auth'
import type { CommunityEvent } from '@/types/content'

const stubs = { RouterLink: RouterLinkStub, teleport: true }
let pinia: Pinia

function ok(body: unknown) {
  return { ok: true, status: 200, statusText: 'OK', json: () => Promise.resolve(body), text: () => Promise.resolve('') } as unknown as Response
}

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  apiJson.mockReset(); apiFetch.mockReset()
  vi.stubGlobal('confirm', vi.fn(() => true))
  route.params = {}; route.query = {}
  router.replace.mockResolvedValue(undefined)
})

describe('MonthRangePicker', () => {
  const mountP = (start: Date, end: Date | null) =>
    mount(MonthRangePicker, { props: { start, end } })

  it('labels a closed range', () => {
    const w = mountP(new Date(2026, 0, 1), new Date(2026, 5, 1))
    expect(w.text()).toContain('Jan 2026')
    expect(w.text()).toContain('Jun 2026')
  })

  it('labels an open-ended range with "onwards"', () => {
    const w = mountP(new Date(2026, 0, 1), null)
    expect(w.text()).toContain('onwards')
  })

  it('emits a start/end range after two picks', async () => {
    const w = mountP(new Date(2026, 0, 1), new Date(2026, 0, 1))
    await w.find('button').trigger('click') // open
    const monthButtons = w.findAll('button').filter(b => b.text() === 'Mar' || b.text() === 'Jun')
    await monthButtons[0].trigger('click') // start
    await monthButtons[1].trigger('click') // end
    expect(w.emitted('change')).toBeTruthy()
    const [start, end] = w.emitted('change')![0] as [Date, Date]
    expect(start).toBeInstanceOf(Date)
    expect(end).toBeInstanceOf(Date)
  })

  it('emits a null end via the "No end date" button (disabled until picking the end)', async () => {
    const w = mountP(new Date(2026, 0, 1), new Date(2026, 0, 1))
    await w.find('button').trigger('click') // open — phase: start
    // The button always reads "No end date" but is disabled until a start month is chosen.
    expect(w.findAll('button').find(b => b.text() === 'No end date')!.attributes('disabled')).toBeDefined()
    await w.findAll('button').find(b => b.text() === 'Mar')!.trigger('click') // pick start — phase: end
    const noEnd = w.findAll('button').find(b => b.text() === 'No end date')!
    expect(noEnd.attributes('disabled')).toBeUndefined()
    await noEnd.trigger('click')
    expect(w.emitted('change')![0][1]).toBeNull()
  })

  it('opens with a null end covering the committedE false branch', async () => {
    const w = mountP(new Date(2026, 0, 1), null)
    await w.find('button').trigger('click') // opens picker — committedE reads null branch
    expect(w.findAll('button').find(b => b.text() === 'No end date')).toBeDefined()
  })

  it('highlights band and button classes when start differs from end (s != e)', async () => {
    const now = new Date()
    const start = new Date(now.getFullYear(), Math.max(1, now.getMonth() - 2), 1)
    const end   = new Date(now.getFullYear(), Math.min(10, now.getMonth() + 3), 1)
    const w = mountP(start, end)
    await w.find('button').trigger('click') // open picker — s != e covers bandClass and btnClass branches
    expect(w.findAll('.relative.py-0\\.5').length).toBeGreaterThan(0)
  })

  it('closes the picker by clicking the trigger button while open', async () => {
    const w = mountP(new Date(2026, 0, 1), new Date(2026, 5, 1))
    await w.find('button').trigger('click') // open
    expect(w.find('[aria-label="Previous year"]').exists()).toBe(true)
    await w.find('button').trigger('click') // click trigger again to close
    expect(w.find('[aria-label="Previous year"]').exists()).toBe(false)
  })
})

describe('EventFormDialog', () => {
  const evt: CommunityEvent = { id: 'e1', title: 'Quiz', type: 'Q&A', startsAt: '2026-06-01T18:00:00Z', endsAt: '2026-06-01T20:00:00Z', location: 'Pub', description: 'Fun', _etag: 'w1' }

  it('adds an event via POST', async () => {
    apiFetch.mockResolvedValue(ok({ id: 'new' }))
    const w = mount(EventFormDialog, { props: { open: true, event: null }, global: { stubs } })
    await w.find('input[type="text"]').setValue('Coffee')
    await w.find('input[type="datetime-local"]').setValue('2026-06-01T10:00')
    await w.findAll('input[type="datetime-local"]')[1].setValue('2026-06-01T12:00')
    await w.findAll('input[type="text"]')[1].setValue('Brixton')
    await w.find('textarea').setValue('Morning coffee')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/events', expect.objectContaining({ method: 'POST' }))
    expect(w.emitted('saved')).toBeTruthy()
  })

  it('edits an event via PUT and prefills fields', async () => {
    apiFetch.mockResolvedValue(ok({ id: 'e1' }))
    const w = mount(EventFormDialog, { props: { open: true, event: evt }, global: { stubs } })
    expect((w.find('input[type="text"]').element as HTMLInputElement).value).toBe('Quiz')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/events/e1', expect.objectContaining({ method: 'PUT' }))
  })

  it('emits close on Cancel', async () => {
    const w = mount(EventFormDialog, { props: { open: true, event: null }, global: { stubs } })
    await w.findAll('button').find(b => b.text() === 'Cancel')!.trigger('click')
    expect(w.emitted('close')).toBeTruthy()
  })

  it('shows an error when the save fails', async () => {
    apiFetch.mockResolvedValue({ ok: false, status: 400, statusText: 'Bad', text: () => Promise.resolve('invalid'), json: () => Promise.resolve({}) } as unknown as Response)
    const w = mount(EventFormDialog, { props: { open: true, event: evt }, global: { stubs } })
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).toContain('400')
  })
})

describe('EventCalendar', () => {
  it('renders the month label and weekday headers', () => {
    const w = mount(EventCalendar, { props: { events: [] }, global: { stubs } })
    expect(w.text()).toContain('Mon')
    expect(w.text()).toContain('Sun')
  })

  it('navigates between months', async () => {
    const w = mount(EventCalendar, { props: { events: [] }, global: { stubs } })
    const label = w.find('h3').text()
    await w.find('button[aria-label="Next month"]').trigger('click')
    expect(w.find('h3').text()).not.toBe(label)
    await w.find('button[aria-label="Previous month"]').trigger('click')
    expect(w.find('h3').text()).toBe(label)
  })

  it('renders an event pill', () => {
    const now = new Date()
    const iso = (d: number, h: number) => new Date(now.getFullYear(), now.getMonth(), d, h).toISOString()
    const w = mount(EventCalendar, {
      props: { events: [{ id: 'e1', title: 'Coffee', type: 'Coffee meet-up', startsAt: iso(15, 10), endsAt: iso(15, 12), location: 'X', description: 'd' }] },
      global: { stubs },
    })
    expect(w.text()).toContain('Coffee')
  })

  it('returns to the current month when Today is clicked', async () => {
    const w = mount(EventCalendar, { props: { events: [] }, global: { stubs } })
    const currentLabel = w.find('h3').text()
    await w.find('button[aria-label="Next month"]').trigger('click')
    expect(w.find('h3').text()).not.toBe(currentLabel)
    await w.findAll('button').find(b => b.text() === 'Today')!.trigger('click')
    expect(w.find('h3').text()).toBe(currentLabel)
  })

  it('renders start/middle/end pill classes for a multi-day event', () => {
    const now = new Date()
    const iso = (d: number, h: number) => new Date(now.getFullYear(), now.getMonth(), d, h).toISOString()
    // Event spans days 15-17: day 15 = start (rounded-r-none), day 16 = middle (rounded-none), day 17 = end (rounded-l-none)
    const w = mount(EventCalendar, {
      props: {
        events: [{ id: 'e1', title: 'Weekend camp', type: 'Activity', startsAt: iso(15, 9), endsAt: iso(17, 17), location: 'X', description: 'd' }],
      },
      global: { stubs },
    })
    const html = w.html()
    expect(html).toContain('rounded-r-none')
    expect(html).toContain('rounded-none')
    expect(html).toContain('rounded-l-none')
  })
})

describe('EventManagementView', () => {
  const evt = (over = {}) => ({ id: 'e1', title: 'Team coffee', type: 'Coffee meet-up', startsAt: new Date().toISOString(), endsAt: new Date(Date.now() + 3600000).toISOString(), location: 'Brixton', description: 'd', _etag: 'w1', ...over })
  const childStubs = {
    ...stubs,
    MonthRangePicker: { emits: ['change'], template: '<div class="mrp"><button @click="$emit(\'change\', new Date(2026, 0, 1), null)">Pick range</button></div>' },
    EventFormDialog: { name: 'EventFormDialog', props: ['open', 'event'], emits: ['close', 'saved'], template: '<div v-if="open" class="event-dialog-open"><button @click="$emit(\'close\')">Close dialog</button></div>' },
  }
  const mountV = () => mount(EventManagementView, { global: { plugins: [pinia], stubs: childStubs } })

  beforeEach(async () => { await useAuthStore().initialize() })

  it('lists events (ignoring the date range)', async () => {
    apiJson.mockResolvedValue([evt()])
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    expect(w.text()).toContain('Team coffee')
  })

  it('opens the add dialog', async () => {
    apiJson.mockResolvedValue([])
    const w = mountV()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Add event'))!.trigger('click')
    expect(w.find('.event-dialog-open').exists()).toBe(true)
  })

  it('deletes an event after confirmation', async () => {
    apiJson.mockResolvedValue([evt()])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/events/e1', expect.objectContaining({ method: 'DELETE' }))
  })

  it('shows a load error', async () => {
    apiJson.mockRejectedValue(new Error('boom'))
    const w = mountV()
    await flushPromises()
    expect(w.text()).toContain('Couldn’t load events')
  })

  it('retries loading events when Retry is clicked after a load error', async () => {
    apiJson.mockRejectedValue(new Error('boom'))
    const w = mountV()
    await flushPromises()
    apiJson.mockResolvedValue([evt()])
    await w.findAll('button').find(b => b.text() === 'Retry')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('Team coffee')
  })

  it('opens the edit dialog when Edit is clicked', async () => {
    apiJson.mockResolvedValue([evt()])
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    await w.findAll('button').find(b => b.text() === 'Edit')!.trigger('click')
    expect(w.find('.event-dialog-open').exists()).toBe(true)
  })

  it('closes the event dialog when the close event fires', async () => {
    apiJson.mockResolvedValue([])
    const w = mountV()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Add event'))!.trigger('click')
    expect(w.find('.event-dialog-open').exists()).toBe(true)
    await w.findAll('button').find(b => b.text() === 'Close dialog')!.trigger('click')
    expect(w.find('.event-dialog-open').exists()).toBe(false)
  })

  it('filters events by search query', async () => {
    apiJson.mockResolvedValue([
      evt({ id: 'e1', title: 'Team coffee', type: 'Coffee meet-up' }),
      evt({ id: 'e2', title: 'Board game night', type: 'Social', location: 'Pub', description: 'fun' }),
    ])
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    await w.find('input[type="search"]').setValue('coffee')
    expect(w.text()).toContain('Team coffee')
    expect(w.text()).not.toContain('Board game night')
  })

  it('shows events with a multi-day date range in the list', async () => {
    const multiDay = evt({ id: 'e1', startsAt: '2026-06-01T10:00:00Z', endsAt: '2026-06-02T12:00:00Z' })
    apiJson.mockResolvedValue([multiDay])
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    expect(w.text()).toContain('Team coffee')
  })

  it('initialises from URL query params — from date and open-ended range (to=any)', async () => {
    route.query = { from: '2026-01', to: 'any' }
    apiJson.mockResolvedValue([])
    const w = mountV()
    await flushPromises()
    // Trigger watch by typing a search — fires watch with rangeEnd=null → router.replace with to='any'
    await w.find('input[type="search"]').setValue('x')
    await flushPromises()
    expect(router.replace).toHaveBeenCalledWith(expect.objectContaining({ query: expect.objectContaining({ to: 'any' }) }))
  })

  it('initialises searchQuery from URL query param q', async () => {
    route.query = { q: 'coffee' }
    apiJson.mockResolvedValue([
      evt({ id: 'e1', title: 'Coffee morning', type: 'Coffee meet-up' }),
      evt({ id: 'e2', title: 'Board games', type: 'Social' }),
    ])
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    expect(w.text()).toContain('Coffee morning')
    expect(w.text()).not.toContain('Board games')
  })

  it('cancels event deletion when confirm returns false', async () => {
    vi.stubGlobal('confirm', vi.fn(() => false))
    apiJson.mockResolvedValue([evt()])
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(apiFetch).not.toHaveBeenCalledWith(expect.stringContaining('/events/e1'), expect.objectContaining({ method: 'DELETE' }))
  })

  it('shows an error when event deletion returns non-ok with a body', async () => {
    apiJson.mockResolvedValue([evt()])
    apiFetch.mockResolvedValue({ ok: false, status: 409, statusText: 'Conflict', text: () => Promise.resolve('version mismatch') } as unknown as Response)
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('version mismatch')
  })

  it('shows delete error as string when rejection is not an Error', async () => {
    apiJson.mockResolvedValue([evt()])
    apiFetch.mockRejectedValue('delete failed')
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('delete failed')
  })

  it('renders events without a type and with a createdByName attribution', async () => {
    const noType = { ...evt(), type: '', createdByName: 'Jo Smith' }
    apiJson.mockResolvedValue([noType])
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    expect(w.text()).toContain('Team coffee')
    expect(w.text()).toContain('Jo Smith')
  })

  it('deletes an event without an etag (no If-Match header)', async () => {
    // eslint-disable-next-line @typescript-eslint/no-unused-vars -- destructuring-omit pattern
    const { _etag: _, ...noEtag } = evt()
    apiJson.mockResolvedValue([noEtag])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/events/e1', expect.objectContaining({ method: 'DELETE', headers: {} }))
  })

  it('shows a generic delete error when the server response body is empty', async () => {
    apiJson.mockResolvedValue([evt()])
    apiFetch.mockResolvedValue({ ok: false, status: 500, statusText: 'Server Error', text: () => Promise.resolve('') } as unknown as Response)
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true)
    await w.findAll('button').find(b => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('500')
  })

  it('hides events outside the date range when allDates is false', async () => {
    // A 2020 event is before any current rangeStart — filter returns false for it
    apiJson.mockResolvedValue([evt({ startsAt: '2020-01-01T10:00:00Z', endsAt: '2020-01-01T12:00:00Z' })])
    const w = mountV()
    await flushPromises()
    // Don't check the "ignore date range" checkbox → allDates=false → line 94 return false
    expect(w.text()).not.toContain('Team coffee')
  })

  it('emits range change from MonthRangePicker and re-enables date filtering', async () => {
    apiJson.mockResolvedValue([evt()])
    const w = mountV()
    await flushPromises()
    await w.find('input[type="checkbox"]').setValue(true) // allDates=true
    await w.find('.mrp button').trigger('click') // emit change(Jan 2026, null) → allDates=false, rangeEnd=null
    await flushPromises()
    expect(router.replace).toHaveBeenCalledWith(expect.objectContaining({ query: expect.objectContaining({ to: 'any' }) }))
  })

  it('refreshes the event list when the form dialog emits saved', async () => {
    apiJson.mockResolvedValue([evt()])
    const w = mountV()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Add event'))!.trigger('click')
    expect(w.find('.event-dialog-open').exists()).toBe(true)
    const dialogComp = w.findComponent({ name: 'EventFormDialog' })
    await dialogComp.vm.$emit('saved')
    await flushPromises()
    expect(apiJson).toHaveBeenCalledTimes(2)
  })
})
