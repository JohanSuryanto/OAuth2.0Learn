import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as accountApi from '../../auth/accountApi'
import type { AccountSummary } from '../../auth/authApi'
import { PasswordSection } from './PasswordSection'

vi.mock('../../auth/accountApi', () => ({
  changePassword: vi.fn(),
  setPassword: vi.fn(),
  reauthWithPassword: vi.fn(),
  reauthWithGoogle: vi.fn(),
}))

const api = vi.mocked(accountApi)
const NEW = 'brand new passphrase'

const account = (methods: AccountSummary['methods']): AccountSummary => ({
  email: 'alice@example.com',
  displayName: null,
  methods,
  createdAt: '2026-09-01T10:00:00Z',
  lastSignInAt: '2026-09-23T08:00:00Z',
})

async function fill({ current, next = NEW, confirm = next }: { current?: string; next?: string; confirm?: string }) {
  if (current !== undefined) await userEvent.type(screen.getByLabelText('Current password'), current)
  await userEvent.type(screen.getByLabelText('New password'), next)
  await userEvent.type(screen.getByLabelText('Confirm new password'), confirm)
}

describe('PasswordSection', () => {
  beforeEach(() => vi.resetAllMocks())

  it('changes the password with the current one', async () => {
    api.changePassword.mockResolvedValue({ ok: true })
    const onChanged = vi.fn()
    render(<PasswordSection account={account(['password'])} onChanged={onChanged} />)

    await fill({ current: 'old passphrase' })
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))

    expect(api.changePassword).toHaveBeenCalledWith('old passphrase', NEW)
    expect(screen.getByRole('status')).toHaveTextContent('Password changed. Other devices have been signed out.')
    expect(onChanged).toHaveBeenCalled()
  })

  it('shows a wrong current password under that field', async () => {
    api.changePassword.mockResolvedValue({ ok: false, reason: 'validation', errors: { currentPassword: 'current_password_incorrect' } })
    render(<PasswordSection account={account(['password'])} onChanged={vi.fn()} />)

    await fill({ current: 'wrong one' })
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))

    expect(screen.getByLabelText('Current password')).toHaveAccessibleDescription('Your current password is incorrect.')
  })

  it('blocks a mismatched confirmation', async () => {
    render(<PasswordSection account={account(['password'])} onChanged={vi.fn()} />)

    await fill({ current: 'old passphrase', confirm: 'something else' })
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))

    expect(screen.getByLabelText('Confirm new password')).toHaveAccessibleDescription("Passwords don't match.")
    expect(api.changePassword).not.toHaveBeenCalled()
  })

  it('sets a password for a Google-only account, confirming with Google first when needed', async () => {
    api.setPassword
      .mockResolvedValueOnce({ ok: false, reason: 'reauth_required', methods: ['google'] })
      .mockResolvedValueOnce({ ok: true })
    api.reauthWithGoogle.mockResolvedValue({ ok: true })
    const onChanged = vi.fn()
    render(<PasswordSection account={account(['google'])} onChanged={onChanged} />)
    expect(screen.queryByLabelText('Current password')).not.toBeInTheDocument()

    await fill({})
    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))

    expect(screen.getByRole('heading', { name: "Confirm it's you" })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Continue with Google' }))

    expect(api.setPassword).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('status')).toHaveTextContent('Password set. You can now sign in with your email and password.')
    expect(onChanged).toHaveBeenCalled()
  })

  it('refuses a different Google account and does not retry', async () => {
    api.setPassword.mockResolvedValue({ ok: false, reason: 'reauth_required', methods: ['google'] })
    api.reauthWithGoogle.mockResolvedValue({ ok: false, reason: 'mismatch' })
    render(<PasswordSection account={account(['google'])} onChanged={vi.fn()} />)

    await fill({})
    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))
    await userEvent.click(screen.getByRole('button', { name: 'Continue with Google' }))

    expect(screen.getByRole('alert')).toHaveTextContent("That Google account isn't the one linked to this account.")
    expect(api.setPassword).toHaveBeenCalledTimes(1)
  })

  it('shows a wrong password in the re-auth panel', async () => {
    api.setPassword.mockResolvedValue({ ok: false, reason: 'reauth_required', methods: ['password'] })
    api.reauthWithPassword.mockResolvedValue({ ok: false, reason: 'validation', errors: { password: 'current_password_incorrect' } })
    render(<PasswordSection account={account(['google'])} onChanged={vi.fn()} />)

    await fill({})
    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))
    await userEvent.type(screen.getByLabelText('Current password'), 'nope')
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }))

    expect(screen.getByLabelText('Current password')).toHaveAccessibleDescription('Your current password is incorrect.')
  })
})
