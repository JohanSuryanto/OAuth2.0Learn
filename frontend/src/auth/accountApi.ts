// Account & security API (spec 004, specs/004-account-settings/contracts/account-api.md).
import type { AccountSummary, MeResult, SessionTimingPayload } from './authApi'
import { failure, getJson, postJson, type ApiFailure } from './http'
import { openLoginPopup, REAUTH_URL } from './loginPopup'

type Ok = { ok: true }

export type SessionInfo = {
  id: string
  device: string | null // null → "Unknown device"
  ipMasked: string | null // null → "Unknown"
  createdAt: string
  lastSeenAt: string
  current: boolean
}

export type ReauthMethod = 'password' | 'google'
export type ReauthRequired = { ok: false; reason: 'reauth_required'; methods: ReauthMethod[] }

export type ProfileResult = { ok: true; account: AccountSummary } | ApiFailure
export type ChangePasswordResult = Ok | ApiFailure
export type SetPasswordResult = Ok | ReauthRequired | ApiFailure
export type ReauthPasswordResult = Ok | ApiFailure
export type GoogleReauthResult = Ok | { ok: false; reason: 'mismatch' | 'cancelled' | 'blocked' | 'failed' }

export async function updateProfile(displayName: string | null): Promise<ProfileResult> {
  const response = await postJson('/api/account/profile', { displayName })
  return response.status === 200 ? { ok: true, account: response.body as AccountSummary } : failure(response)
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<ChangePasswordResult> {
  const response = await postJson('/api/account/password/change', { currentPassword, newPassword })
  return response.status === 204 ? { ok: true } : failure(response)
}

export async function setPassword(newPassword: string): Promise<SetPasswordResult> {
  const response = await postJson('/api/account/password/set', { newPassword })
  if (response.status === 204) return { ok: true }
  const body = response.body as { code?: string; methods?: ReauthMethod[] } | undefined
  if (response.status === 403 && body?.code === 'reauth_required') {
    return { ok: false, reason: 'reauth_required', methods: body.methods ?? [] }
  }
  return failure(response)
}

export async function reauthWithPassword(password: string): Promise<ReauthPasswordResult> {
  const response = await postJson('/api/account/reauth/password', { password })
  return response.status === 204 ? { ok: true } : failure(response)
}

/** Confirms identity with a fresh Google sign-in in the popup; the current session is left untouched. */
export async function reauthWithGoogle(): Promise<GoogleReauthResult> {
  const result = await openLoginPopup(() => {}, REAUTH_URL)
  if (result.success) return result.reauth ? { ok: true } : { ok: false, reason: 'failed' }
  switch (result.error) {
    case 'reauth_mismatch':
      return { ok: false, reason: 'mismatch' }
    case 'access_denied':
    case 'timeout':
      return { ok: false, reason: 'cancelled' }
    case 'popup_blocked':
      return { ok: false, reason: 'blocked' }
    default:
      return { ok: false, reason: 'failed' }
  }
}

export async function listSessions(): Promise<SessionInfo[] | null> {
  const response = await getJson('/api/account/sessions')
  return response.status === 200 ? (response.body as SessionInfo[]) : null
}

export async function revokeSession(id: string): Promise<boolean> {
  return (await postJson(`/api/account/sessions/${encodeURIComponent(id)}/revoke`, {})).status === 204
}

export async function revokeOtherSessions(): Promise<boolean> {
  return (await postJson('/api/account/sessions/revoke-others', {})).status === 204
}

/** "Stay signed in": a fresh 60-minute idle window (never past the 8-hour limit). */
export async function extendSession(): Promise<MeResult | null> {
  const response = await postJson('/api/auth/session/extend', {})
  if (response.status !== 200) return null
  const body = response.body as { email: string; name: string | null; session?: SessionTimingPayload | null }
  return { user: { email: body.email, name: body.name }, session: body.session ?? null }
}
