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
})
