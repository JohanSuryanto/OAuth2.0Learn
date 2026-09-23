import { createContext, useContext } from 'react'

export type LoginDialogValue = {
  isOpen: boolean
  /** Opens the sign-in dialog; focus returns to the element that opened it when it closes. */
  open: () => void
  close: () => void
}

export const LoginDialogContext = createContext<LoginDialogValue | null>(null)

export function useLoginDialog(): LoginDialogValue {
  const context = useContext(LoginDialogContext)
  if (!context) throw new Error('useLoginDialog must be used inside <LoginDialogProvider>')
  return context
}
