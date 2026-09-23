// The sign-in popup lands here (same origin as the dashboard) after the backend finishes the OAuth flow.
// It reports the result to the dashboard and closes itself. See contracts/auth-api.md.
;(function () {
  var allowed = ['success', 'access_denied', 'signin_failed', 'reauth_ok', 'reauth_mismatch']
  var result = new URLSearchParams(window.location.search).get('result')
  if (allowed.indexOf(result) === -1) result = 'signin_failed'

  var ok = result === 'success' || result === 'reauth_ok'
  var message = {
    type: 'oauth-result',
    success: ok,
    // reauth: identity confirmed without signing in (spec 004, research R6).
    reauth: result === 'reauth_ok' ? true : undefined,
    error: ok ? undefined : result,
  }

  // Primary path: works even when Google's Cross-Origin-Opener-Policy has cut window.opener.
  try {
    var channel = new BroadcastChannel('oauth-login')
    channel.postMessage(message)
    channel.close()
  } catch {
    // BroadcastChannel unavailable; fall through to postMessage.
  }

  // Secondary path.
  try {
    if (window.opener) window.opener.postMessage(message, window.location.origin)
  } catch {
    // Opener gone or cross-origin; nothing else to do.
  }

  window.close()
})()
