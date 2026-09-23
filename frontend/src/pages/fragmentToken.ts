/**
 * Reads `#token=...` from the URL once and removes it from the address bar (research R5): fragments are never
 * sent to servers or in Referer headers, and removing it keeps the token out of history and screenshots.
 */
export function takeTokenFromFragment(): string {
  const token = new URLSearchParams(window.location.hash.slice(1)).get('token') ?? ''
  if (window.location.hash) {
    window.history.replaceState(null, '', window.location.pathname + window.location.search)
  }
  return token
}
