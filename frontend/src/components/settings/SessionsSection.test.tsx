import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as accountApi from '../../auth/accountApi'
import { SessionsSection } from './SessionsSection'

vi.mock('../../auth/accountApi', () => ({
  listSessions: vi.fn(),
  revokeSession: vi.fn(),
  revokeOtherSessions: vi.fn(),
}))

const api = vi.mocked(accountApi)

const session = (overrides: Partial<accountApi.SessionInfo>): accountApi.SessionInfo => ({
  id: 'x',
  device: 'Chrome on Windows',
  ipMasked: '127.0.0.x',
  createdAt: '2026-09-23T08:00:00Z',
  lastSeenAt: '2026-09-23T09:00:00Z',
  current: false,
  ...overrides,
})

const rows = () => screen.getAllByTestId('session-row')

describe('SessionsSection', () => {
  beforeEach(() => vi.resetAllMocks())

  it('lists this device first, marked, without a Sign out button', async () => {
    api.listSessions.mockResolvedValue([
      session({ id: 'me', current: true }),
      session({ id: 'other', device: 'Firefox on Linux' }),
    ])
    render(<SessionsSection />)

    await screen.findAllByTestId('session-row')
    expect(within(rows()[0]).getByText('This device')).toBeInTheDocument()
    expect(within(rows()[0]).queryByRole('button', { name: /Sign out/ })).not.toBeInTheDocument()
    expect(within(rows()[1]).getByRole('button', { name: 'Sign out Firefox on Linux' })).toBeInTheDocument()
    expect(rows()[0]).toHaveTextContent('127.0.0.x')
  })

  it('signs out another device and removes its row', async () => {
    api.listSessions.mockResolvedValue([session({ id: 'me', current: true }), session({ id: 'other' })])
    api.revokeSession.mockResolvedValue(true)
    render(<SessionsSection />)
    await screen.findAllByTestId('session-row')

    await userEvent.click(within(rows()[1]).getByRole('button', { name: /Sign out/ }))

    expect(api.revokeSession).toHaveBeenCalledWith('other')
    expect(rows()).toHaveLength(1)
  })

  it('hides "Sign out all other devices" with a single session', async () => {
    api.listSessions.mockResolvedValue([session({ id: 'me', current: true })])
    render(<SessionsSection />)
    await screen.findAllByTestId('session-row')

    expect(screen.queryByRole('button', { name: 'Sign out all other devices' })).not.toBeInTheDocument()
  })

  it('signs out all other devices and reloads the list', async () => {
    api.listSessions
      .mockResolvedValueOnce([session({ id: 'me', current: true }), session({ id: 'b' }), session({ id: 'c' })])
      .mockResolvedValueOnce([session({ id: 'me', current: true })])
    api.revokeOtherSessions.mockResolvedValue(true)
    render(<SessionsSection />)
    await screen.findAllByTestId('session-row')

    await userEvent.click(screen.getByRole('button', { name: 'Sign out all other devices' }))

    expect(api.revokeOtherSessions).toHaveBeenCalledTimes(1)
    expect(api.listSessions).toHaveBeenCalledTimes(2)
    await screen.findByText('This device')
    expect(rows()).toHaveLength(1)
  })

  it('shows "Unknown device" when the device is unknown', async () => {
    api.listSessions.mockResolvedValue([session({ id: 'me', current: true, device: null, ipMasked: null })])
    render(<SessionsSection />)

    expect(await screen.findByText('Unknown device')).toBeInTheDocument()
    expect(screen.getByText('Unknown')).toBeInTheDocument()
  })

  it('shows an error when the list cannot be loaded', async () => {
    api.listSessions.mockResolvedValue(null)
    render(<SessionsSection />)

    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't update your sessions. Please try again.")
  })
})
