// Shared request helpers for the /api calls. Everything goes through the Vite proxy (same origin), and every
// POST carries the X-Requested-With header the backend requires as CSRF protection.

export type FieldErrors = Record<string, string>

export type ApiFailure =
  | { ok: false; reason: 'too_many_attempts' }
  | { ok: false; reason: 'validation'; errors: FieldErrors }
  | { ok: false; reason: 'unexpected' }

export type RawResponse = { status: number; body: unknown }

async function send(url: string, init: RequestInit): Promise<RawResponse> {
  try {
    const response = await fetch(url, { credentials: 'include', ...init })
    const text = await response.text()
    let parsed: unknown = undefined
    if (text) {
      try {
        parsed = JSON.parse(text)
      } catch {
        parsed = undefined
      }
    }
    return { status: response.status, body: parsed }
  } catch {
    return { status: 0, body: undefined } // network error / backend down
  }
}

export function postJson(url: string, body: unknown): Promise<RawResponse> {
  return send(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
    body: JSON.stringify(body),
  })
}

export function getJson(url: string): Promise<RawResponse> {
  return send(url, { method: 'GET' })
}

/** Shared mapping for 400 { errors } and 429 { code: "too_many_attempts" }; anything else is unexpected. */
export function failure({ status, body }: RawResponse): ApiFailure {
  if (status === 429) return { ok: false, reason: 'too_many_attempts' }
  const errors = (body as { errors?: unknown } | undefined)?.errors
  if (status === 400 && errors && typeof errors === 'object') {
    return { ok: false, reason: 'validation', errors: errors as FieldErrors }
  }
  return { ok: false, reason: 'unexpected' }
}
