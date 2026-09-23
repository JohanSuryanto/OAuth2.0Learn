import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { LoginDialogProvider } from './LoginDialog'
import { useLoginDialog } from './loginDialogContext'
import type { AuthContextValue } from './useAuth'

const auth: AuthContextValue = {
  state: { status: 'signedOut' },
  login: vi.fn(async () => {}),
  logout: vi.fn(async () => {}),
  refresh: vi.fn(async () => {}),
  clearError: vi.fn(),
  extendSession: vi.fn(async () => true),
}

vi.mock('./useAuth', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./useAuth')>()),
  useAuth: () => auth,
}))
vi.mock('./passwordAuthApi', () => ({}))

function Opener() {
  const { open } = useLoginDialog()
  return (
    <>
      <button type="button" onClick={open}>
        Open A
      </button>
      <button type="button" onClick={() => (open(), open())}>
        Open twice
      </button>
    </>
  )
}

function renderProvider() {
  return render(
    <LoginDialogProvider>
      <Opener />
    </LoginDialogProvider>,
  )
}

describe('LoginDialogProvider', () => {
  it('opens the sign-in dialog and returns focus to the opener on close', async () => {
    renderProvider()
    const opener = screen.getByRole('button', { name: 'Open A' })

    await userEvent.click(opener)
    expect(screen.getByRole('dialog', { name: 'Sign in' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(opener).toHaveFocus()
  })

  it('renders a single dialog even when opened twice', async () => {
    renderProvider()

    await userEvent.click(screen.getByRole('button', { name: 'Open twice' }))

    expect(screen.getAllByRole('dialog')).toHaveLength(1)
  })
})
