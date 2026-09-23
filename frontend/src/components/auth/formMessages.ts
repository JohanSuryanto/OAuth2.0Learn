// UI texts for the email/password flows (contract "UI messages" table).

export const FORM_MESSAGES = {
  generic: 'Something went wrong. Please try again.',
  tooManyAttempts: 'Too many attempts. Please try again later.',
  invalidCredentials: 'Email or password is incorrect.',
  emailNotVerified: "Please verify your email first. We've sent you a new link.",
  checkInbox: (email: string) => `Check your inbox — we've sent a verification link to ${email}.`,
  resetSent: (email: string) => `If an account exists for ${email}, we've sent a reset link.`,
  passwordChanged: 'Your password has been changed. You can now sign in.',
  verifyLinkInvalid: 'This link is no longer valid. Sign in to get a new one.',
  resetLinkInvalid: 'This link is no longer valid. Request a new one.',
} as const

const FIELD_MESSAGES: Record<string, string> = {
  email_invalid: 'Enter a valid email address.',
  password_too_short: 'Use at least 10 characters.',
  password_too_long: 'Use at most 128 characters.',
  password_is_email: "Your password can't be your email address.",
  password_common: 'This password is too common. Choose a less predictable one.',
  display_name_too_long: 'Use at most 100 characters.',
  password_mismatch: "That's not the password you chose when registering.",
  token_invalid: 'This link is no longer valid.',
  current_password_incorrect: 'Your current password is incorrect.',
  no_password: "This account doesn't have a password yet. Set one instead.",
  password_already_set: 'This account already has a password. Change it instead.',
}

/** Message for a server validation code; unknown codes fall back to the generic message. */
export function fieldMessage(code: string): string {
  return FIELD_MESSAGES[code] ?? FORM_MESSAGES.generic
}

/** Form-level message for failures that aren't tied to a field. */
export function failureMessage(reason: string): string {
  return reason === 'too_many_attempts' ? FORM_MESSAGES.tooManyAttempts : FORM_MESSAGES.generic
}
