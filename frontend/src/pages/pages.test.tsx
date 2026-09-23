import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactElement } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../auth/passwordAuthApi'
import type { AuthContextValue } from '../auth/useAuth'
import { FORM_MESSAGES } from '../components/auth/formMessages'
import { ResetPasswordPage } from './ResetPasswordPage'
import { VerifyEmailPage } from './VerifyEmailPage'

const auth: AuthContextValue = {
  state: { status: 'signedOut' },
  login: vi.fn(async () => {}),
  logout: vi.fn(async () => {}),
  refresh: vi.fn(async () => {}),
  clearError: vi.fn(),
  extendSession: vi.fn(async () => true),
}

vi.mock('../auth/useAuth', () => ({ useAuth: () => auth }))
vi.mock('../auth/passwordAuthApi', () => ({ verifyEmail: vi.fn(), resetPassword: vi.fn() }))

const verifyEmail = vi.mocked(api.verifyEmail)
const resetPassword = vi.mocked(api.resetPassword)

function renderPage(page: ReactElement) {
  return render(
    <MemoryRouter initialEntries={['/page']}>
      <Routes>
        <Route path="/page" element={page} />
        <Route path="/dashboard" element={<p>DASHBOARD</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

function openAt(path: string, fragment: string) {
  window.history.replaceState(null, '', `${path}${fragment}`)
}

describe('VerifyEmailPage', () => {
  beforeEach(() => vi.resetAllMocks())

  it('reads the token from the fragment and removes it from the URL', async () => {
    verifyEmail.mockResolvedValue({ ok: true })
    openAt('/verify-email', '#token=abc123')
    renderPage(<VerifyEmailPage />)

    expect(window.location.hash).toBe('')
    expect(window.location.pathname).toBe('/verify-email')

    await userEvent.type(screen.getByLabelText('Password you chose'), 'my chosen password')
    await userEvent.click(screen.getByRole('button', { name: 'Verify and sign in' }))

    expect(verifyEmail).toHaveBeenCalledWith('abc123', 'my chosen password')
    expect(auth.refresh).toHaveBeenCalled()
    expect(screen.getByText('DASHBOARD')).toBeInTheDocument() // lands on the Dashboard (spec 003, FR-004)
  })

  it('shows a field error when the password is not the chosen one', async () => {
    verifyEmail.mockResolvedValue({ ok: false, reason: 'validation', errors: { password: 'password_mismatch' } })
    openAt('/verify-email', '#token=abc123')
    renderPage(<VerifyEmailPage />)

    await userEvent.type(screen.getByLabelText('Password you chose'), 'wrong one')
    await userEvent.click(screen.getByRole('button', { name: 'Verify and sign in' }))

    expect(screen.getByLabelText('Password you chose')).toHaveAccessibleDescription(
      "That's not the password you chose when registering.",
    )
  })

  it('hides the form when the link is no longer valid', async () => {
    verifyEmail.mockResolvedValue({ ok: false, reason: 'validation', errors: { token: 'token_invalid' } })
    openAt('/verify-email', '#token=old')
    renderPage(<VerifyEmailPage />)

    await userEvent.type(screen.getByLabelText('Password you chose'), 'whatever pw')
    await userEvent.click(screen.getByRole('button', { name: 'Verify and sign in' }))

    expect(screen.getByRole('alert')).toHaveTextContent(FORM_MESSAGES.verifyLinkInvalid)
    expect(screen.queryByRole('form')).not.toBeInTheDocument()
  })

  it('treats a missing token as an invalid link', () => {
    openAt('/verify-email', '')
    renderPage(<VerifyEmailPage />)

    expect(screen.getByRole('alert')).toHaveTextContent(FORM_MESSAGES.verifyLinkInvalid)
  })
})

describe('ResetPasswordPage', () => {
  beforeEach(() => vi.resetAllMocks())

  it('blocks a mismatched confirmation client-side', async () => {
    openAt('/reset-password', '#token=t1')
    renderPage(<ResetPasswordPage />)

    await userEvent.type(screen.getByLabelText('New password'), 'brand new passphrase')
    await userEvent.type(screen.getByLabelText('Confirm new password'), 'something else')
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))

    expect(screen.getByLabelText('Confirm new password')).toHaveAccessibleDescription("Passwords don't match.")
    expect(resetPassword).not.toHaveBeenCalled()
  })

  it('changes the password with the token from the fragment', async () => {
    resetPassword.mockResolvedValue({ ok: true })
    openAt('/reset-password', '#token=t1')
    renderPage(<ResetPasswordPage />)
    expect(window.location.hash).toBe('')

    await userEvent.type(screen.getByLabelText('New password'), 'brand new passphrase')
    await userEvent.type(screen.getByLabelText('Confirm new password'), 'brand new passphrase')
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))

    expect(resetPassword).toHaveBeenCalledWith('t1', 'brand new passphrase')
    expect(screen.getByRole('status')).toHaveTextContent(FORM_MESSAGES.passwordChanged)
  })

  it('shows server password errors and invalid links', async () => {
    resetPassword.mockResolvedValueOnce({ ok: false, reason: 'validation', errors: { newPassword: 'password_common' } })
    resetPassword.mockResolvedValueOnce({ ok: false, reason: 'validation', errors: { token: 'token_invalid' } })
    openAt('/reset-password', '#token=t1')
    renderPage(<ResetPasswordPage />)

    await userEvent.type(screen.getByLabelText('New password'), 'basketball')
    await userEvent.type(screen.getByLabelText('Confirm new password'), 'basketball')
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))
    expect(screen.getByLabelText('New password')).toHaveAccessibleDescription(
      'This password is too common. Choose a less predictable one.',
    )

    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))
    expect(screen.getByRole('alert')).toHaveTextContent(FORM_MESSAGES.resetLinkInvalid)
  })
})
