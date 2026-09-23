export type LoginError = 'access_denied' | 'signin_failed' | 'popup_blocked' | 'timeout' | 'reauth_mismatch'
/** reauth is true when the popup confirmed identity without signing in (spec 004, research R6). */
export type LoginResult = { success: true; reauth?: boolean } | { success: false; error: LoginError }

export const LOGIN_URL = `${import.meta.env.VITE_API_ORIGIN}/api/auth/login`
export const REAUTH_URL = `${import.meta.env.VITE_API_ORIGIN}/api/auth/reauth/google`

/** Channel used by public/auth-complete.js to report the result (research R2). */
export const LOGIN_CHANNEL = 'oauth-login'
export const LOGIN_TIMEOUT_MS = 5 * 60 * 1000
const POLL_INTERVAL_MS = 500
const POPUP_WIDTH = 500
const POPUP_HEIGHT = 650

/**
 * `window.open` features that center the popup over the current browser window
 * (works on multi-monitor setups because it uses the window's own screen position).
 */
export function centeredPopupFeatures(width = POPUP_WIDTH, height = POPUP_HEIGHT): string {
  // Clamp to the browser window's own edge (not 0): on a monitor left of the primary one,
  // screen coordinates are negative and clamping to 0 would jump to another monitor.
  const left = Math.round(window.screenX + Math.max(window.outerWidth - width, 0) / 2)
  const top = Math.round(window.screenY + Math.max(window.outerHeight - height, 0) / 2)
  return `width=${width},height=${height},left=${left},top=${top}`
}

type PendingLogin = {
  popup: Window
  promise: Promise<LoginResult>
  settle: (result: LoginResult) => void
}

let pending: PendingLogin | null = null

/**
 * Opens the Google sign-in popup and resolves with its result.
 *
 * `popup.closed` is only a hint: Google's Cross-Origin-Opener-Policy can make it read `true`
 * while the user is still signing in, so it triggers `onPopupClosed` (a session re-check)
 * but never ends the flow. The flow ends on an `oauth-result` message or after the timeout.
 */
export function openLoginPopup(onPopupClosed: () => void, url: string = LOGIN_URL): Promise<LoginResult> {
  if (pending && !pending.popup.closed) {
    pending.popup.focus()
    return pending.promise
  }
  // An older flow whose popup is gone: settle it before starting over.
  pending?.settle({ success: false, error: 'timeout' })

  const popup = window.open(url, 'google-login', centeredPopupFeatures())
  if (!popup) return Promise.resolve({ success: false, error: 'popup_blocked' })

  let settle: (result: LoginResult) => void = () => {}
  const promise = new Promise<LoginResult>((resolve) => {
    const channel = new BroadcastChannel(LOGIN_CHANNEL)
    let done = false
    let closedNotified = false

    const onWindowMessage = (event: MessageEvent) => {
      if (event.origin !== window.location.origin) return
      const result = parseResult(event.data)
      if (result) settle(result)
    }

    const poll = setInterval(() => {
      if (!closedNotified && popup.closed) {
        closedNotified = true
        onPopupClosed()
      }
    }, POLL_INTERVAL_MS)

    const timer = setTimeout(() => settle({ success: false, error: 'timeout' }), LOGIN_TIMEOUT_MS)

    settle = (result) => {
      if (done) return
      done = true
      channel.close()
      window.removeEventListener('message', onWindowMessage)
      clearInterval(poll)
      clearTimeout(timer)
      if (pending?.popup === popup) pending = null
      resolve(result)
    }

    channel.onmessage = (event: MessageEvent) => {
      const result = parseResult(event.data)
      if (result) settle(result)
    }
    window.addEventListener('message', onWindowMessage)
  })

  pending = { popup, promise, settle }
  return promise
}

function parseResult(data: unknown): LoginResult | null {
  if (typeof data !== 'object' || data === null) return null
  const message = data as { type?: unknown; success?: unknown; error?: unknown; reauth?: unknown }
  if (message.type !== 'oauth-result') return null
  if (message.success === true) return message.reauth === true ? { success: true, reauth: true } : { success: true }
  const error: LoginError =
    message.error === 'access_denied' ? 'access_denied' : message.error === 'reauth_mismatch' ? 'reauth_mismatch' : 'signin_failed'
  return { success: false, error }
}
