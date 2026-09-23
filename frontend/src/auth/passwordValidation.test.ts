import { describe, expect, it } from 'vitest'
import { normalizeEmail, validateDisplayName, validateEmail, validateNewPassword } from './passwordValidation'

describe('passwordValidation', () => {
  it('normalizes email by trimming and lower-casing', () => {
    expect(normalizeEmail('  Alice@Example.COM ')).toBe('alice@example.com')
  })

  it.each([
    ['', 'Enter your email address.'],
    ['   ', 'Enter your email address.'],
    ['alice', 'Enter a valid email address.'],
    ['alice@example', 'Enter a valid email address.'],
    ['a b@example.com', 'Enter a valid email address.'],
    ['alice@example.com', undefined],
    [' alice@example.com ', undefined],
  ])('validateEmail(%j)', (email, expected) => {
    expect(validateEmail(email)).toBe(expected)
  })

  it('requires 10 to 128 characters', () => {
    expect(validateNewPassword('a'.repeat(9), 'x@example.com')).toBe('Use at least 10 characters.')
    expect(validateNewPassword('a'.repeat(10), 'x@example.com')).toBeUndefined()
    expect(validateNewPassword('a'.repeat(128), 'x@example.com')).toBeUndefined()
    expect(validateNewPassword('a'.repeat(129), 'x@example.com')).toBe('Use at most 128 characters.')
  })

  it('rejects a password equal to the email, ignoring case and spaces', () => {
    expect(validateNewPassword('Alice@Example.com', ' alice@example.com')).toBe(
      "Your password can't be your email address.",
    )
  })

  it('imposes no composition rules', () => {
    expect(validateNewPassword('correct horse battery staple', 'x@example.com')).toBeUndefined()
  })

  it('limits the display name to 100 characters', () => {
    expect(validateDisplayName('a'.repeat(100))).toBeUndefined()
    expect(validateDisplayName('a'.repeat(101))).toBe('Use at most 100 characters.')
  })
})
