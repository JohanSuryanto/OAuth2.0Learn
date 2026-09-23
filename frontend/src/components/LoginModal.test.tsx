import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthContextValue, AuthState } from '../auth/useAuth'
import { LoginModal } from './LoginModal'

const auth: AuthContextValue = {
  state: { status: 'signedOut' },
  login: vi.fn(async () => {}),
  logout: vi.fn(async () => {}),
  refresh: vi.fn(async () => {}),
  clearError: vi.fn(),
  extendSession: vi.fn(async () => true),
}

vi.mock('../auth/useAuth', () => ({ useAuth: () => auth }))
vi.mock('../auth/passwordAuthApi', () => ({
  register: vi.fn(async () => ({ ok: true })),
  forgotPassword: vi.fn(async () => ({ ok: true })),
  signInWithPassword: vi.fn(async () => ({ ok: true })),
}))

function renderModal(state: AuthState = { status: 'signedOut' }) {
  auth.state = state
  const onClose = vi.fn()
  const utils = render(<LoginModal onClose={onClose} />)
  return { onClose, ...utils }
}

describe('LoginModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('offers Continue with Google, focuses it, and clears old errors on open', () => {
    renderModal()

    const google = screen.getByRole('button', { name: 'Continue with Google' })
    expect(screen.getByRole('dialog', { name: 'Sign in' })).toBeInTheDocument()
    expect(google).toHaveFocus()
    expect(auth.clearError).toHaveBeenCalledTimes(1)
  })

  it('starts the Google login when Continue with Google is clicked', async () => {
    let finish: () => void = () => {}
    vi.mocked(auth.login).mockImplementation(() => new Promise<void>((resolve) => (finish = resolve)))
    renderModal()

    await userEvent.click(screen.getByRole('button', { name: 'Continue with Google' }))

    expect(auth.login).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('status')).toHaveTextContent('Finish signing in in the Google window')
    finish()
  })

  it('shows a sign-in error inside the dialog', () => {
    renderModal({ status: 'signedOut', error: 'Sign-in was cancelled.' })

    expect(screen.getByRole('alert')).toHaveTextContent('Sign-in was cancelled.')
  })

  it('closes on Escape, the close button, and a backdrop click', async () => {
    const { onClose, container } = renderModal()

    await userEvent.keyboard('{Escape}')
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    await userEvent.click(container.querySelector('.modal-backdrop')!)

    expect(onClose).toHaveBeenCalledTimes(3)
  })

  it('does not close when clicking inside the dialog', async () => {
    const { onClose } = renderModal()

    await userEvent.click(screen.getByRole('heading', { name: 'Sign in' }))

    expect(onClose).not.toHaveBeenCalled()
  })

  it('shows the email sign-in form below Google, separated by "or"', () => {
    renderModal()

    expect(screen.getByRole('separator')).toHaveTextContent('or')
    expect(screen.getByRole('form', { name: 'Sign in with email' })).toBeInTheDocument()
  })

  it('switches between Sign in and Create account, keeping Google available', async () => {
    renderModal()

    await userEvent.click(screen.getByRole('button', { name: 'Create one' }))
    expect(screen.getByRole('dialog', { name: 'Create account' })).toBeInTheDocument()
    expect(screen.getByRole('form', { name: 'Create account' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue with Google' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(screen.getByRole('dialog', { name: 'Sign in' })).toBeInTheDocument()
    expect(screen.getByRole('form', { name: 'Sign in with email' })).toBeInTheDocument()
  })

  it('shows "Check your inbox" after registering, and goes back to sign in', async () => {
    renderModal()
    await userEvent.click(screen.getByRole('button', { name: 'Create one' }))

    await userEvent.type(screen.getByLabelText('Email'), 'new@example.com')
    await userEvent.type(screen.getByLabelText('Password'), 'long enough pw')
    await userEvent.type(screen.getByLabelText('Confirm password'), 'long enough pw')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

    expect(screen.getByRole('dialog', { name: 'Check your inbox' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(
      "Check your inbox — we've sent a verification link to new@example.com.",
    )
    expect(screen.queryByRole('button', { name: 'Continue with Google' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Back to sign in' }))
    expect(screen.getByRole('form', { name: 'Sign in with email' })).toBeInTheDocument()
  })

  it('opens the reset view from "Forgot password?" and returns', async () => {
    renderModal()

    await userEvent.click(screen.getByRole('button', { name: 'Forgot password?' }))
    expect(screen.getByRole('dialog', { name: 'Reset your password' })).toBeInTheDocument()
    expect(screen.getByRole('form', { name: 'Reset password' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Continue with Google' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Back to sign in' }))
    expect(screen.getByRole('dialog', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('closes itself once the user is signed in', () => {
    const { onClose } = renderModal({ status: 'signedIn', user: { email: 'a@b.com', name: null }, session: null })

    expect(onClose).toHaveBeenCalled()
  })
})
