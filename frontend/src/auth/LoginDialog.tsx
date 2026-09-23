import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react'
import { LoginModal } from '../components/LoginModal'
import { LoginDialogContext } from './loginDialogContext'

/** One sign-in dialog for the whole app, opened by the header's Login and Home's "Get started" (spec 003, R6). */
export function LoginDialogProvider({ children }: { children: ReactNode }) {
  const [isOpen, setIsOpen] = useState(false)
  const opener = useRef<HTMLElement | null>(null)

  const open = useCallback(() => {
    opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    setIsOpen(true)
  }, [])

  const close = useCallback(() => {
    setIsOpen(false)
    // The opener may have been replaced (e.g. Login → Logout after signing in); only refocus if still on the page.
    if (opener.current?.isConnected) opener.current.focus()
    opener.current = null
  }, [])

  const value = useMemo(() => ({ isOpen, open, close }), [isOpen, open, close])

  return (
    <LoginDialogContext.Provider value={value}>
      {children}
      {isOpen && <LoginModal onClose={close} />}
    </LoginDialogContext.Provider>
  )
}
