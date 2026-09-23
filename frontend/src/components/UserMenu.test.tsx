import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AuthContextValue } from '../auth/useAuth'
import { UserMenu } from './UserMenu'

const auth: AuthContextValue = {
  state: { status: 'signedIn', user: { email: 'a@b.com', name: 'Alice' }, session: null },
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

function renderMenu(name: string | null = 'Alice') {
  auth.state = { status: 'signedIn', user: { email: 'a@b.com', name }, session: null }
  return render(
    <MemoryRouter initialEntries={['/dashboard']}>
      <p>outside</p>
      <UserMenu />
      <Routes>
        <Route path="/" element={<p>HOME</p>} />
        <Route path="/dashboard" element={<p>DASHBOARD</p>} />
        <Route path="/settings" element={<p>SETTINGS</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

const menuButton = () => screen.getByRole('button', { expanded: false }) ?? screen.getByRole('button')
const items = () => screen.getAllByRole('menuitem')

describe('UserMenu', () => {
  beforeEach(() => vi.clearAllMocks())

  it('is labelled with the display name, or the email without one', () => {
    renderMenu('Alice')
    expect(screen.getByRole('button', { name: /Alice/ })).toBeInTheDocument()
  })

  it('falls back to the email', () => {
    renderMenu(null)
    expect(screen.getByRole('button', { name: /a@b\.com/ })).toBeInTheDocument()
  })

  it('opens on click with the email and three choices', async () => {
    renderMenu()

    await userEvent.click(menuButton())

    expect(screen.getByRole('menu')).toBeInTheDocument()
    expect(screen.getByRole('menu')).toHaveTextContent('a@b.com')
    expect(items().map((i) => i.textContent)).toEqual(['Dashboard', 'Account settings', 'Log out'])
    expect(screen.getByRole('button', { name: /Alice/ })).toHaveAttribute('aria-expanded', 'true')
  })

  it.each(['{Enter}', ' ', '{ArrowDown}'])('opens with %s and focuses the first item', async (key) => {
    renderMenu()
    screen.getByRole('button', { name: /Alice/ }).focus()

    await userEvent.keyboard(key)

    await waitFor(() => expect(items()[0]).toHaveFocus())
  })

  it('opens with ArrowUp on the last item', async () => {
    renderMenu()
    screen.getByRole('button', { name: /Alice/ }).focus()

    await userEvent.keyboard('{ArrowUp}')

    await waitFor(() => expect(items()[2]).toHaveFocus())
  })

  it('moves with arrows (wrapping) and Home/End', async () => {
    renderMenu()
    screen.getByRole('button', { name: /Alice/ }).focus()
    await userEvent.keyboard('{ArrowDown}')
    await waitFor(() => expect(items()[0]).toHaveFocus())

    await userEvent.keyboard('{ArrowUp}')
    expect(items()[2]).toHaveFocus()
    await userEvent.keyboard('{ArrowDown}')
    expect(items()[0]).toHaveFocus()
    await userEvent.keyboard('{End}')
    expect(items()[2]).toHaveFocus()
    await userEvent.keyboard('{Home}')
    expect(items()[0]).toHaveFocus()
  })

  it('closes on Escape and returns focus to the button', async () => {
    renderMenu()
    const button = screen.getByRole('button', { name: /Alice/ })
    button.focus()
    await userEvent.keyboard('{ArrowDown}')
    await waitFor(() => expect(items()[0]).toHaveFocus())

    await userEvent.keyboard('{Escape}')

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(button).toHaveFocus()
  })

  it('closes on a click outside', async () => {
    renderMenu()
    await userEvent.click(menuButton())

    fireEvent.mouseDown(screen.getByText('outside'))

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('opens Account settings', async () => {
    renderMenu()
    await userEvent.click(menuButton())

    await userEvent.click(screen.getByRole('menuitem', { name: 'Account settings' }))

    expect(screen.getByText('SETTINGS')).toBeInTheDocument()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('logs out and goes Home', async () => {
    renderMenu()
    await userEvent.click(menuButton())

    await userEvent.click(screen.getByRole('menuitem', { name: 'Log out' }))

    expect(auth.logout).toHaveBeenCalledTimes(1)
    expect(screen.getByText('HOME')).toBeInTheDocument()
  })
})
