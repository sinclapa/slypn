import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', () => ({ apiJson, apiFetch }))
const router = { push: vi.fn(), replace: vi.fn().mockResolvedValue(undefined), back: vi.fn() }
vi.mock('vue-router', async (orig) => {
  const actual = await (orig() as Promise<Record<string, unknown>>)
  return { ...actual, useRoute: () => ({ params: {}, query: {} }), useRouter: () => router }
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

  it('emits a null end via the end-date button (labelled "Select end date" until picking the end)', async () => {
    const w = mountP(new Date(2026, 0, 1), new Date(2026, 0, 1))
    await w.find('button').trigger('click') // open — phase: start
    // In the start phase the button prompts "Select end date" and is disabled.
    const prompt = w.findAll('button').find(b => b.text() === 'Select end date')!
    expect(prompt.attributes('disabled')).toBeDefined()
    await w.findAll('button').find(b => b.text() === 'Mar')!.trigger('click') // pick start — phase: end
    const noEnd = w.findAll('button').find(b => b.text() === 'No end date')!
    expect(noEnd.attributes('disabled')).toBeUndefined()
    await noEnd.trigger('click')
    expect(w.emitted('change')![0][1]).toBeNull()
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
})

describe('EventManagementView', () => {
  const evt = (over = {}) => ({ id: 'e1', title: 'Team coffee', type: 'Coffee meet-up', startsAt: new Date().toISOString(), endsAt: new Date(Date.now() + 3600000).toISOString(), location: 'Brixton', description: 'd', _etag: 'w1', ...over })
  const childStubs = {
    ...stubs,
    MonthRangePicker: { template: '<div class="mrp" />' },
    EventFormDialog: { props: ['open', 'event'], template: '<div v-if="open" class="event-dialog-open" />' },
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
})
