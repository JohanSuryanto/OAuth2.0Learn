import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as accountApi from '../auth/accountApi'
import * as authApi from '../auth/authApi'
import type { AuthContextValue } from '../auth/useAuth'
import { SettingsPage } from './SettingsPage'

const auth: AuthContextValue = {
  state: { status: 'signedIn', user: { email: 'alice@example.com', name: 'Alice' }, session: null },
  login: vi.fn(async () => {}),
  logout: vi.fn(async () => {}),
  refresh: vi.fn(async () => {}),
  clearError: vi.fn(),
  extendSession: vi.fn(async () => true),
}

vi.mock('../auth/useAuth', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../auth/useAuth')>()),
  useAuth: () => auth,
}))
vi.mock('../auth/authApi', () => ({ fetchAccount: vi.fn() }))
vi.mock('../auth/accountApi', () => ({
  updateProfile: vi.fn(),
  changePassword: vi.fn(),
  setPassword: vi.fn(),
  reauthWithPassword: vi.fn(),
  reauthWithGoogle: vi.fn(),
  listSessions: vi.fn(async () => []),
  revokeSession: vi.fn(),
  revokeOtherSessions: vi.fn(),
}))

const account = (overrides: Partial<authApi.AccountSummary> = {}): authApi.AccountSummary => ({
  email: 'alice@example.com',
  displayName: 'Alice',
  methods: ['google'],
  createdAt: '2026-09-01T10:00:00Z',
  lastSignInAt: '2026-09-23T08:00:00Z',
  ...overrides,
})

async function renderSettings(summary = account()) {
  vi.mocked(authApi.fetchAccount).mockResolvedValue(summary)
  render(<SettingsPage />)
  await screen.findByRole('heading', { name: 'Account settings' })
}

const profile = () => screen.getByRole('form', { name: 'Profile' })

describe('SettingsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(accountApi.listSessions).mockResolvedValue([])
  })

  it('shows the email as read-only text', async () => {
    await renderSettings()

    expect(within(profile()).getByText('alice@example.com')).toBeInTheDocument()
    expect(within(profile()).getByText("Your email can't be changed.")).toBeInTheDocument()
    expect(within(profile()).queryByDisplayValue('alice@example.com')).not.toBeInTheDocument()
    expect(within(profile()).getByLabelText('Display name')).toHaveValue('Alice')
  })

  it('saves a trimmed display name and refreshes the header', async () => {
    vi.mocked(accountApi.updateProfile).mockResolvedValue({ ok: true, account: account({ displayName: 'Alicia' }) })
    await renderSettings()
    const field = within(profile()).getByLabelText('Display name')

    await userEvent.clear(field)
    await userEvent.type(field, '  Alicia  ')
    await userEvent.click(within(profile()).getByRole('button', { name: 'Save' }))

    expect(accountApi.updateProfile).toHaveBeenCalledWith('Alicia')
    expect(await within(profile()).findByRole('status')).toHaveTextContent('Profile updated.')
    expect(auth.refresh).toHaveBeenCalled()
  })

  it('removes the name when the field is cleared', async () => {
    vi.mocked(accountApi.updateProfile).mockResolvedValue({ ok: true, account: account({ displayName: null }) })
    await renderSettings()

    await userEvent.clear(within(profile()).getByLabelText('Display name'))
    await userEvent.click(within(profile()).getByRole('button', { name: 'Save' }))

    expect(accountApi.updateProfile).toHaveBeenCalledWith(null)
  })

  it('refuses names over 100 characters without calling the server', async () => {
    await renderSettings()
    const field = within(profile()).getByLabelText('Display name')

    await userEvent.clear(field)
    await userEvent.type(field, 'a'.repeat(101))
    await userEvent.click(within(profile()).getByRole('button', { name: 'Save' }))

    expect(field).toHaveAccessibleDescription('Use at most 100 characters.')
    expect(accountApi.updateProfile).not.toHaveBeenCalled()
  })

  it('lists sign-in methods read-only', async () => {
    await renderSettings(account({ methods: ['google'] }))

    const section = screen.getByRole('region', { name: 'Sign-in methods' })
    expect(section).toHaveTextContent('GoogleConnected')
    expect(section).toHaveTextContent('PasswordNot set')
    expect(within(section).queryByRole('button')).not.toBeInTheDocument()
  })

  it('offers "Set a password" to Google-only accounts and "Change password" otherwise', async () => {
    await renderSettings(account({ methods: ['google'] }))
    expect(screen.getByRole('heading', { name: 'Set a password' })).toBeInTheDocument()
  })

  it('offers "Change password" when a password exists', async () => {
    await renderSettings(account({ methods: ['password'] }))
    expect(screen.getByRole('heading', { name: 'Change password' })).toBeInTheDocument()
  })
})
