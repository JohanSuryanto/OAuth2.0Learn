import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { forgotPassword, register, resetPassword, signInWithPassword, verifyEmail } from './passwordAuthApi'

function respond(status: number, body?: unknown) {
  return new Response(body === undefined ? null : JSON.stringify(body), { status })
}

describe('passwordAuthApi', () => {
  const fetchMock = vi.fn()

  beforeEach(() => {
    fetchMock.mockReset()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => vi.unstubAllGlobals())

  it('posts JSON with credentials and the CSRF header', async () => {
    fetchMock.mockResolvedValue(respond(202, {}))

    await register({ email: 'a@b.com', password: 'long enough pw' })

    expect(fetchMock).toHaveBeenCalledWith('/api/auth/register', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
      body: JSON.stringify({ email: 'a@b.com', password: 'long enough pw' }),
    })
  })

  it('maps sign-in statuses', async () => {
    fetchMock.mockResolvedValueOnce(respond(204))
    fetchMock.mockResolvedValueOnce(respond(401, { code: 'invalid_credentials' }))
    fetchMock.mockResolvedValueOnce(respond(403, { code: 'email_not_verified' }))
    fetchMock.mockResolvedValueOnce(respond(429, { code: 'too_many_attempts' }))
    fetchMock.mockResolvedValueOnce(respond(500))

    expect(await signInWithPassword('a@b.com', 'pw')).toEqual({ ok: true })
    expect(await signInWithPassword('a@b.com', 'pw')).toEqual({ ok: false, reason: 'invalid_credentials' })
    expect(await signInWithPassword('a@b.com', 'pw')).toEqual({ ok: false, reason: 'email_not_verified' })
    expect(await signInWithPassword('a@b.com', 'pw')).toEqual({ ok: false, reason: 'too_many_attempts' })
    expect(await signInWithPassword('a@b.com', 'pw')).toEqual({ ok: false, reason: 'unexpected' })
  })

  it('maps validation errors', async () => {
    fetchMock.mockResolvedValue(respond(400, { errors: { token: 'token_invalid' } }))

    expect(await verifyEmail('t', 'pw')).toEqual({ ok: false, reason: 'validation', errors: { token: 'token_invalid' } })
  })

  it('treats network failures as unexpected', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    expect(await forgotPassword('a@b.com')).toEqual({ ok: false, reason: 'unexpected' })
  })

  it('reset succeeds on 204', async () => {
    fetchMock.mockResolvedValue(respond(204))

    expect(await resetPassword('t', 'new passphrase')).toEqual({ ok: true })
    expect(fetchMock.mock.calls[0][0]).toBe('/api/auth/reset-password')
  })
})
