import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, RouterLinkStub, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'

const { apiJson, apiFetch } = vi.hoisted(() => ({ apiJson: vi.fn(), apiFetch: vi.fn() }))
vi.mock('@/lib/api', () => ({ apiJson, apiFetch }))

import MemberManagementView from './MemberManagementView.vue'
import { useAuthStore } from '@/stores/auth'

const stubs = { RouterLink: RouterLinkStub }
let pinia: Pinia
const mountC = () => mount(MemberManagementView, { global: { plugins: [pinia], stubs } })

function ok(body: unknown) {
  return { ok: true, status: 200, statusText: 'OK', json: () => Promise.resolve(body), text: () => Promise.resolve('') } as unknown as Response
}

const member = (over = {}) => ({
  id: 'm1', email: 'a@b.com', displayName: 'Alice', roles: ['Member'],
  status: 'active', invitedAt: '2026-04-01T00:00:00Z', oid: 'oidA', _etag: 'e1', ...over,
})

beforeEach(async () => {
  pinia = createPinia()
  setActivePinia(pinia)
  apiJson.mockReset()
  apiFetch.mockReset()
  vi.stubGlobal('confirm', vi.fn(() => true))
  await useAuthStore().initialize()
})

describe('MemberManagementView', () => {
  it('lists members', async () => {
    apiJson.mockResolvedValue([member()])
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('Alice')
    expect(w.text()).toContain('a@b.com')
  })

  it('changes a member role via PATCH', async () => {
    apiJson.mockResolvedValue([member()])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountC()
    await flushPromises()
    const adminBtn = w.findAll('button').find(b => b.text() === 'Admin')!
    await adminBtn.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/members/m1', expect.objectContaining({ method: 'PATCH' }))
  })

  it('removes a member after confirmation', async () => {
    apiJson.mockResolvedValue([member()])
    apiFetch.mockResolvedValue(ok({}))
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Remove')!.trigger('click')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/members/m1', expect.objectContaining({ method: 'DELETE' }))
  })

  it('disables removing yourself', async () => {
    // admin persona oid
    apiJson.mockResolvedValue([member({ id: 'me', oid: '11111111-1111-1111-1111-111111111111' })])
    const w = mountC()
    await flushPromises()
    const removeBtn = w.findAll('button').find(b => b.text() === 'Remove')!
    expect(removeBtn.attributes('disabled')).toBeDefined()
  })

  it('sends an invite and shows the redeem link', async () => {
    apiJson.mockResolvedValue([])
    apiFetch.mockResolvedValue(ok({ member: { email: 'new@x.com' }, inviteSent: true, redeemUrl: 'https://redeem/abc', inviteReason: null }))
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Invite a member'))!.trigger('click')
    await w.find('input[type="email"]').setValue('new@x.com')
    await w.find('input[type="text"]').setValue('New Person')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/members/invite', expect.objectContaining({ method: 'POST' }))
    expect(w.text()).toContain('https://redeem/abc')
  })

  it('shows the empty state', async () => {
    apiJson.mockResolvedValue([])
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain('No members yet')
  })

  it('shows a load error', async () => {
    apiJson.mockRejectedValue(new Error('boom'))
    const w = mountC()
    await flushPromises()
    expect(w.text()).toContain("Couldn't load members")
  })

  it('sorts members alphabetically when there are multiple', async () => {
    apiJson.mockResolvedValue([
      member({ id: 'm2', displayName: 'Zara', email: 'z@b.com' }),
      member({ id: 'm1', displayName: 'Alice', email: 'a@b.com' }),
    ])
    const w = mountC()
    await flushPromises()
    const names = w.findAll('td, th, p, span').map(el => el.text()).join(' ')
    expect(names.indexOf('Alice')).toBeLessThan(names.indexOf('Zara'))
  })

  it('shows a save error when setRole returns non-ok', async () => {
    apiJson.mockResolvedValue([member()])
    apiFetch.mockResolvedValue({ ok: false, status: 403, statusText: 'Forbidden', text: () => Promise.resolve('') } as unknown as Response)
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Admin')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('403')
  })

  it('shows a save error when deleteMember returns non-ok', async () => {
    apiJson.mockResolvedValue([member()])
    apiFetch.mockResolvedValue({ ok: false, status: 409, statusText: 'Conflict', text: () => Promise.resolve('') } as unknown as Response)
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Remove')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('409')
  })

  it('dismisses a save error by clicking Dismiss', async () => {
    apiJson.mockResolvedValue([member()])
    apiFetch.mockResolvedValue({ ok: false, status: 403, statusText: 'Forbidden', text: () => Promise.resolve('') } as unknown as Response)
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Admin')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('403')
    await w.findAll('button').find(b => b.text() === 'Dismiss')!.trigger('click')
    expect(w.text()).not.toContain('403')
  })

  it('retries loading members when Retry is clicked after a load error', async () => {
    apiJson.mockRejectedValue(new Error('boom'))
    const w = mountC()
    await flushPromises()
    apiJson.mockResolvedValue([member()])
    await w.findAll('button').find(b => b.text() === 'Retry')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('Alice')
  })

  it('shows an invite error when the invite API returns non-ok', async () => {
    apiJson.mockResolvedValue([])
    apiFetch.mockResolvedValue({ ok: false, status: 400, statusText: 'Bad Request', text: () => Promise.resolve('Email already in use') } as unknown as Response)
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Invite a member'))!.trigger('click')
    await w.find('input[type="email"]').setValue('dup@x.com')
    await w.find('input[type="text"]').setValue('Someone')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).toContain('400')
  })

  it('can switch the invite role to Admin before submitting', async () => {
    apiJson.mockResolvedValue([])
    apiFetch.mockResolvedValue(ok({ member: { email: 'admin@x.com' }, inviteSent: true, redeemUrl: null, inviteReason: null }))
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text()?.includes('Invite a member'))!.trigger('click')
    await w.findAll('button').find(b => b.text() === 'Admin')!.trigger('click')
    await w.find('input[type="email"]').setValue('admin@x.com')
    await w.find('input[type="text"]').setValue('Admin Person')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(apiFetch).toHaveBeenCalledWith('/members/invite', expect.objectContaining({ method: 'POST' }))
  })

  it('cancels member deletion when confirm returns false', async () => {
    vi.stubGlobal('confirm', vi.fn(() => false))
    apiJson.mockResolvedValue([member()])
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Remove')!.trigger('click')
    await flushPromises()
    expect(apiFetch).not.toHaveBeenCalledWith('/members/m1', expect.objectContaining({ method: 'DELETE' }))
  })

  it('ignores setRole when another save is already in progress', async () => {
    apiJson.mockResolvedValue([member()])
    let settle!: (r: Response) => void
    apiFetch.mockImplementationOnce(() => new Promise<Response>(r => { settle = r }))
    const w = mountC()
    await flushPromises()
    const adminBtn = w.findAll('button').find(b => b.text() === 'Admin')!
    adminBtn.trigger('click') // first — starts PATCH, sets savingId
    await adminBtn.trigger('click') // second — savingId set, early return
    expect(apiFetch).toHaveBeenCalledTimes(1)
    settle(ok({}))
  })

  it('ignores deleteMember when another delete is already in progress', async () => {
    apiJson.mockResolvedValue([member()])
    let settle!: (r: Response) => void
    apiFetch.mockImplementationOnce(() => new Promise<Response>(r => { settle = r }))
    const w = mountC()
    await flushPromises()
    const removeBtn = w.findAll('button').find(b => b.text() === 'Remove')!
    removeBtn.trigger('click') // first — sets deletingId
    await removeBtn.trigger('click') // second — deletingId set, early return
    expect(apiFetch).toHaveBeenCalledTimes(1)
    settle(ok({}))
  })

  it('shows setRole error as string when rejection is not an Error', async () => {
    apiJson.mockResolvedValue([member()])
    apiFetch.mockRejectedValue('patch failed')
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Admin')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('patch failed')
  })

  it('shows deleteMember error as string when rejection is not an Error', async () => {
    apiJson.mockResolvedValue([member()])
    apiFetch.mockRejectedValue('delete member failed')
    const w = mountC()
    await flushPromises()
    await w.findAll('button').find(b => b.text() === 'Remove')!.trigger('click')
    await flushPromises()
    expect(w.text()).toContain('delete member failed')
  })
})
