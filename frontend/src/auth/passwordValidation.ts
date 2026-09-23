// Client-side checks mirroring spec 002 (FR-003, FR-004, FR-005). The server will re-check everything;
// these only give fast feedback. The common-password check is server-side only.

export const PASSWORD_MIN_LENGTH = 10
export const PASSWORD_MAX_LENGTH = 128
export const DISPLAY_NAME_MAX_LENGTH = 100

export function normalizeEmail(email: string): string {
  return email.trim().toLowerCase()
}

export function validateEmail(email: string): string | undefined {
  const value = email.trim()
  if (!value) return 'Enter your email address.'
  // Deliberately loose: something@something.something. The server decides what's deliverable.
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) return 'Enter a valid email address.'
  return undefined
}

export function validateNewPassword(password: string, email: string): string | undefined {
  if (password.length < PASSWORD_MIN_LENGTH) return `Use at least ${PASSWORD_MIN_LENGTH} characters.`
  if (password.length > PASSWORD_MAX_LENGTH) return `Use at most ${PASSWORD_MAX_LENGTH} characters.`
  if (email.trim() && password.trim().toLowerCase() === normalizeEmail(email)) {
    return "Your password can't be your email address."
  }
  return undefined
}

export function validateDisplayName(name: string): string | undefined {
  if (name.trim().length > DISPLAY_NAME_MAX_LENGTH) return `Use at most ${DISPLAY_NAME_MAX_LENGTH} characters.`
  return undefined
}
