// Email/password API (spec 002, specs/002-email-password-auth/contracts/password-auth-api.md).
// Calls go through the Vite proxy (same origin), with the X-Requested-With header the backend requires
// as CSRF protection.

import { failure, postJson, type ApiFailure } from './http'

export type { ApiFailure, FieldErrors } from './http'

type Ok = { ok: true }

export type RegisterResult = Ok | ApiFailure
export type VerifyEmailResult = Ok | ApiFailure
export type SignInResult = Ok | { ok: false; reason: 'invalid_credentials' | 'email_not_verified' } | ApiFailure
export type ForgotPasswordResult = Ok | ApiFailure
export type ResetPasswordResult = Ok | ApiFailure

export type RegisterRequest = {
  email: string
  password: string
  displayName?: string
}

export async function register(request: RegisterRequest): Promise<RegisterResult> {
  const response = await postJson('/api/auth/register', request)
  return response.status === 202 ? { ok: true } : failure(response)
}

export async function verifyEmail(token: string, password: string): Promise<VerifyEmailResult> {
  const response = await postJson('/api/auth/verify-email', { token, password })
  return response.status === 204 ? { ok: true } : failure(response)
}

export async function signInWithPassword(email: string, password: string): Promise<SignInResult> {
  const response = await postJson('/api/auth/password/sign-in', { email, password })
  if (response.status === 204) return { ok: true }
  if (response.status === 401) return { ok: false, reason: 'invalid_credentials' }
  if (response.status === 403) return { ok: false, reason: 'email_not_verified' }
  return failure(response)
}

export async function forgotPassword(email: string): Promise<ForgotPasswordResult> {
  const response = await postJson('/api/auth/forgot-password', { email })
  return response.status === 202 ? { ok: true } : failure(response)
}

export async function resetPassword(token: string, newPassword: string): Promise<ResetPasswordResult> {
  const response = await postJson('/api/auth/reset-password', { token, newPassword })
  return response.status === 204 ? { ok: true } : failure(response)
}
