import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { useAuth } from '../auth/useAuth'

/**
 * Header menu for signed-in users (spec 004, FR-001–FR-003), following the WAI-ARIA "menu button" pattern:
 * Enter/Space/ArrowDown open on the first item, ArrowUp on the last; arrows wrap; Home/End jump;
 * Escape closes and returns focus to the button; Tab or a click outside closes.
 */
export function UserMenu() {
  const { state, logout } = useAuth()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const button = useRef<HTMLButtonElement>(null)
  const container = useRef<HTMLDivElement>(null)
  const items = useRef<(HTMLElement | null)[]>([])
  const menuId = useId()

  useEffect(() => {
    if (!open) return
    const onMouseDown = (event: MouseEvent) => {
      if (!container.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onMouseDown)
    return () => document.removeEventListener('mousedown', onMouseDown)
  }, [open])

  if (state.status !== 'signedIn') return null
  const { email, name } = state.user

  const focusItem = (index: number) => {
    const count = items.current.length
    items.current[((index % count) + count) % count]?.focus()
  }

  const openAt = (index: number) => {
    setOpen(true)
    // Items render on the next frame.
    requestAnimationFrame(() => focusItem(index))
  }

  const close = (refocus: boolean) => {
    setOpen(false)
    if (refocus) button.current?.focus()
  }

  const onButtonKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      openAt(0)
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      openAt(-1)
    }
  }

  const onMenuKeyDown = (event: KeyboardEvent) => {
    const current = items.current.indexOf(document.activeElement as HTMLElement)
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault()
        focusItem(current + 1)
        break
      case 'ArrowUp':
        event.preventDefault()
        focusItem(current - 1)
        break
      case 'Home':
        event.preventDefault()
        focusItem(0)
        break
      case 'End':
        event.preventDefault()
        focusItem(-1)
        break
      case 'Escape':
        event.preventDefault()
        close(true)
        break
      case 'Tab':
        close(false)
        break
    }
  }

  const onLogout = async () => {
    close(false)
    await logout()
    navigate('/')
  }

  return (
    <div className="user-menu" ref={container}>
      <button
        ref={button}
        type="button"
        className="user-menu__button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={menuId}
        onClick={() => (open ? close(false) : setOpen(true))}
        onKeyDown={onButtonKeyDown}
      >
        <span className="user-menu__label">{name || email}</span>
        <span aria-hidden="true">▾</span>
      </button>

      {open && (
        <div id={menuId} className="user-menu__list" role="menu" aria-label="Account" onKeyDown={onMenuKeyDown}>
          <div className="user-menu__email" role="presentation">
            {email}
          </div>
          <Link
            ref={(el) => {
              items.current[0] = el
            }}
            to="/dashboard"
            role="menuitem"
            tabIndex={-1}
            className="user-menu__item"
            onClick={() => close(false)}
          >
            Dashboard
          </Link>
          <Link
            ref={(el) => {
              items.current[1] = el
            }}
            to="/settings"
            role="menuitem"
            tabIndex={-1}
            className="user-menu__item"
            onClick={() => close(false)}
          >
            Account settings
          </Link>
          <button
            ref={(el) => {
              items.current[2] = el
            }}
            type="button"
            role="menuitem"
            tabIndex={-1}
            className="user-menu__item"
            onClick={() => void onLogout()}
          >
            Log out
          </button>
        </div>
      )}
    </div>
  )
}
