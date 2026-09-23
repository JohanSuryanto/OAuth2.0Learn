import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../auth/passwordAuthApi'
import { EmailSignInForm } from './EmailSignInForm'
import { ForgotPasswordForm } from './ForgotPasswordForm'
import { FORM_MESSAGES } from './formMessages'
import { RegisterForm } from './RegisterForm'

vi.mock('../../auth/passwordAuthApi', () => ({
  signInWithPassword: vi.fn(),
  register: vi.fn(),
  forgotPassword: vi.fn(),
}))

const signInWithPassword = vi.mocked(api.signInWithPassword)
const register = vi.mocked(api.register)
const forgotPassword = vi.mocked(api.forgotPassword)

describe('EmailSignInForm', () => {
  beforeEach(() => vi.resetAllMocks())

  function renderForm(onSignedIn = vi.fn(), onForgotPassword = vi.fn()) {
    render(<EmailSignInForm onSignedIn={onSignedIn} onForgotPassword={onForgotPassword} />)
    return { onSignedIn, onForgotPassword }
  }

  async function submit(email = 'a@b.com', password = 'secret password') {
    await userEvent.type(screen.getByLabelText('Email'), email)
    await userEvent.type(screen.getByLabelText('Password'), password)
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
  }

  it('validates before submitting', async () => {
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(screen.getByLabelText('Email')).toHaveAccessibleDescription('Enter your email address.')
    expect(screen.getByLabelText('Password')).toHaveAccessibleDescription('Enter your password.')
    expect(signInWithPassword).not.toHaveBeenCalled()
  })

  it('submits trimmed email and the password, then reports success', async () => {
    signInWithPassword.mockResolvedValue({ ok: true })
    const { onSignedIn } = renderForm()

    await submit('  a@b.com ', 'secret password')

    expect(signInWithPassword).toHaveBeenCalledWith('a@b.com', 'secret password')
    expect(onSignedIn).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['invalid_credentials', FORM_MESSAGES.invalidCredentials],
    ['email_not_verified', FORM_MESSAGES.emailNotVerified],
    ['too_many_attempts', FORM_MESSAGES.tooManyAttempts],
    ['unexpected', FORM_MESSAGES.generic],
  ] as const)('shows the right message for %s and clears the password', async (reason, message) => {
    signInWithPassword.mockResolvedValue({ ok: false, reason } as api.SignInResult)
    const { onSignedIn } = renderForm()

    await submit()

    expect(screen.getByRole('alert')).toHaveTextContent(message)
    expect(screen.getByLabelText('Password')).toHaveValue('')
    expect(onSignedIn).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled()
  })

  it('offers Forgot password?', async () => {
    const { onForgotPassword } = renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Forgot password?' }))

    expect(onForgotPassword).toHaveBeenCalledTimes(1)
  })

  it('lets the user show and hide the password', async () => {
    renderForm()
    const password = screen.getByLabelText('Password')
    expect(password).toHaveAttribute('type', 'password')

    await userEvent.click(screen.getByRole('button', { name: 'Show password' }))
    expect(password).toHaveAttribute('type', 'text')

    await userEvent.click(screen.getByRole('button', { name: 'Hide password' }))
    expect(password).toHaveAttribute('type', 'password')
  })

  it('uses autocomplete hints that password managers understand', () => {
    renderForm()

    expect(screen.getByLabelText('Email')).toHaveAttribute('autocomplete', 'username')
    expect(screen.getByLabelText('Password')).toHaveAttribute('autocomplete', 'current-password')
  })
})

describe('RegisterForm', () => {
  beforeEach(() => vi.resetAllMocks())

  type FillOptions = { name?: string; email?: string; password?: string; confirm?: string }

  async function fill({ name = '', email = 'new@example.com', password = 'long enough pw', confirm }: FillOptions = {}) {
    confirm ??= password
    if (name) await userEvent.type(screen.getByLabelText('Display name (optional)'), name)
    if (email) await userEvent.type(screen.getByLabelText('Email'), email)
    if (password) await userEvent.type(screen.getByLabelText('Password'), password)
    if (confirm) await userEvent.type(screen.getByLabelText('Confirm password'), confirm)
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
  }

  it('rejects a short password', async () => {
    render(<RegisterForm onCheckInbox={vi.fn()} />)

    await fill({ password: 'short', confirm: 'short' })

    expect(screen.getByLabelText('Password')).toHaveAccessibleDescription('Use at least 10 characters.')
    expect(register).not.toHaveBeenCalled()
  })

  it('rejects mismatched passwords', async () => {
    render(<RegisterForm onCheckInbox={vi.fn()} />)

    await fill({ password: 'long enough pw', confirm: 'something else' })

    expect(screen.getByLabelText('Confirm password')).toHaveAccessibleDescription("Passwords don't match.")
    expect(register).not.toHaveBeenCalled()
  })

  it('rejects a password equal to the email', async () => {
    render(<RegisterForm onCheckInbox={vi.fn()} />)

    await fill({ email: 'new@example.com', password: 'new@example.com' })

    expect(screen.getByLabelText('Password')).toHaveAccessibleDescription("Your password can't be your email address.")
  })

  it('submits trimmed values and asks the user to check their inbox', async () => {
    register.mockResolvedValue({ ok: true })
    const onCheckInbox = vi.fn()
    render(<RegisterForm onCheckInbox={onCheckInbox} />)

    await fill({ email: ' new@example.com ' })

    expect(register).toHaveBeenCalledWith({ email: 'new@example.com', password: 'long enough pw', displayName: undefined })
    expect(onCheckInbox).toHaveBeenCalledWith('new@example.com')
  })

  it('sends the display name when given', async () => {
    register.mockResolvedValue({ ok: true })
    render(<RegisterForm onCheckInbox={vi.fn()} />)

    await fill({ name: '  Alice ' })

    expect(register).toHaveBeenCalledWith(expect.objectContaining({ displayName: 'Alice' }))
  })

  it('shows server field errors under the matching field', async () => {
    register.mockResolvedValue({ ok: false, reason: 'validation', errors: { password: 'password_common' } })
    const onCheckInbox = vi.fn()
    render(<RegisterForm onCheckInbox={onCheckInbox} />)

    await fill()

    expect(screen.getByLabelText('Password')).toHaveAccessibleDescription(
      'This password is too common. Choose a less predictable one.',
    )
    expect(onCheckInbox).not.toHaveBeenCalled()
  })

  it('shows the throttling message', async () => {
    register.mockResolvedValue({ ok: false, reason: 'too_many_attempts' })
    render(<RegisterForm onCheckInbox={vi.fn()} />)

    await fill()

    expect(screen.getByRole('alert')).toHaveTextContent(FORM_MESSAGES.tooManyAttempts)
  })

  it('shows the password rules as a hint and uses new-password autocomplete', () => {
    render(<RegisterForm onCheckInbox={vi.fn()} />)

    const password = screen.getByLabelText('Password')
    expect(password).toHaveAccessibleDescription('10–128 characters. A long passphrase works well.')
    expect(password).toHaveAttribute('autocomplete', 'new-password')
  })
})

describe('ForgotPasswordForm', () => {
  beforeEach(() => vi.resetAllMocks())

  it('validates the email', async () => {
    render(<ForgotPasswordForm onBack={vi.fn()} />)

    await userEvent.type(screen.getByLabelText('Email'), 'nope')
    await userEvent.click(screen.getByRole('button', { name: 'Send reset link' }))

    expect(screen.getByLabelText('Email')).toHaveAccessibleDescription('Enter a valid email address.')
    expect(forgotPassword).not.toHaveBeenCalled()
  })

  it('always shows the same confirmation after sending', async () => {
    forgotPassword.mockResolvedValue({ ok: true })
    const onBack = vi.fn()
    render(<ForgotPasswordForm onBack={onBack} />)

    await userEvent.type(screen.getByLabelText('Email'), ' a@b.com ')
    await userEvent.click(screen.getByRole('button', { name: 'Send reset link' }))

    expect(forgotPassword).toHaveBeenCalledWith('a@b.com')
    expect(screen.getByRole('status')).toHaveTextContent(FORM_MESSAGES.resetSent('a@b.com'))
    await userEvent.click(screen.getByRole('button', { name: 'Back to sign in' }))
    expect(onBack).toHaveBeenCalled()
  })
})
