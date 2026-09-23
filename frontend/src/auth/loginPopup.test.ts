import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

class FakeChannel {
  static instances: FakeChannel[] = []
  onmessage: ((event: MessageEvent) => void) | null = null
  closed = false
  name: string

  constructor(name: string) {
    this.name = name
    FakeChannel.instances.push(this)
  }

  postMessage() {}

  close() {
    this.closed = true
  }

  emit(data: unknown) {
    this.onmessage?.({ data } as MessageEvent)
  }
}

type FakePopup = { closed: boolean; focus: ReturnType<typeof vi.fn> }

async function loadModule() {
  vi.resetModules()
  return import('./loginPopup')
}

function trackSettled<T>(promise: Promise<T>) {
  const state = { settled: false }
  void promise.then(() => (state.settled = true))
  return state
}

const flush = () => Promise.resolve().then(() => Promise.resolve())

describe('openLoginPopup', () => {
  let popup: FakePopup
  let openSpy: ReturnType<typeof vi.fn>

  beforeEach(() => {
    FakeChannel.instances = []
    vi.stubGlobal('BroadcastChannel', FakeChannel)
    popup = { closed: false, focus: vi.fn() }
    openSpy = vi.fn(() => popup)
    vi.stubGlobal('open', openSpy)
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('reports popup_blocked when window.open returns null', async () => {
    openSpy.mockReturnValue(null)
    const { openLoginPopup } = await loadModule()

    await expect(openLoginPopup(() => {})).resolves.toEqual({ success: false, error: 'popup_blocked' })
  })

  it('opens the backend login URL and resolves success from the BroadcastChannel', async () => {
    const { openLoginPopup, LOGIN_CHANNEL } = await loadModule()

    const promise = openLoginPopup(() => {})
    expect(openSpy).toHaveBeenCalledWith('https://localhost:5001/api/auth/login', 'google-login', expect.any(String))
    const channel = FakeChannel.instances[0]
    expect(channel.name).toBe(LOGIN_CHANNEL)

    channel.emit({ type: 'oauth-result', success: true })

    await expect(promise).resolves.toEqual({ success: true })
    expect(channel.closed).toBe(true)
  })

  it('maps error results and treats unknown errors as signin_failed', async () => {
    const { openLoginPopup } = await loadModule()

    const denied = openLoginPopup(() => {})
    FakeChannel.instances[0].emit({ type: 'oauth-result', success: false, error: 'access_denied' })
    await expect(denied).resolves.toEqual({ success: false, error: 'access_denied' })

    const failed = openLoginPopup(() => {})
    FakeChannel.instances[1].emit({ type: 'oauth-result', success: false, error: 'weird' })
    await expect(failed).resolves.toEqual({ success: false, error: 'signin_failed' })
  })

  it('opens the re-authentication URL and reports reauth results (spec 004)', async () => {
    const { openLoginPopup, REAUTH_URL } = await loadModule()

    const ok = openLoginPopup(() => {}, REAUTH_URL)
    expect(openSpy).toHaveBeenCalledWith('https://localhost:5001/api/auth/reauth/google', 'google-login', expect.any(String))
    FakeChannel.instances[0].emit({ type: 'oauth-result', success: true, reauth: true })
    await expect(ok).resolves.toEqual({ success: true, reauth: true })

    const mismatch = openLoginPopup(() => {}, REAUTH_URL)
    FakeChannel.instances[1].emit({ type: 'oauth-result', success: false, error: 'reauth_mismatch' })
    await expect(mismatch).resolves.toEqual({ success: false, error: 'reauth_mismatch' })
  })

  it('ignores window messages from a foreign origin', async () => {
    const { openLoginPopup } = await loadModule()

    const promise = openLoginPopup(() => {})
    const state = trackSettled(promise)
    window.dispatchEvent(
      new MessageEvent('message', { origin: 'https://evil.example', data: { type: 'oauth-result', success: true } }),
    )
    await flush()
    expect(state.settled).toBe(false)

    window.dispatchEvent(
      new MessageEvent('message', { origin: window.location.origin, data: { type: 'oauth-result', success: true } }),
    )
    await expect(promise).resolves.toEqual({ success: true })
  })

  it('treats popup.closed only as a hint and keeps listening', async () => {
    vi.useFakeTimers()
    const { openLoginPopup } = await loadModule()
    const onPopupClosed = vi.fn()

    const promise = openLoginPopup(onPopupClosed)
    const state = trackSettled(promise)
    popup.closed = true
    vi.advanceTimersByTime(600)
    vi.advanceTimersByTime(2000)
    await flush()

    expect(onPopupClosed).toHaveBeenCalledTimes(1)
    expect(state.settled).toBe(false)

    FakeChannel.instances[0].emit({ type: 'oauth-result', success: true })
    await expect(promise).resolves.toEqual({ success: true })
  })

  it('resolves timeout after 5 minutes without a result', async () => {
    vi.useFakeTimers()
    const { openLoginPopup, LOGIN_TIMEOUT_MS } = await loadModule()

    const promise = openLoginPopup(() => {})
    vi.advanceTimersByTime(LOGIN_TIMEOUT_MS)

    await expect(promise).resolves.toEqual({ success: false, error: 'timeout' })
  })

  it('centers the popup over the current browser window', async () => {
    vi.stubGlobal('screenX', 100)
    vi.stubGlobal('screenY', 50)
    vi.stubGlobal('outerWidth', 1500)
    vi.stubGlobal('outerHeight', 1050)
    const { openLoginPopup } = await loadModule()

    void openLoginPopup(() => {})

    // left = 100 + (1500 - 500) / 2, top = 50 + (1050 - 650) / 2
    expect(openSpy).toHaveBeenCalledWith(expect.any(String), 'google-login', 'width=500,height=650,left=600,top=250')
  })

  it('aligns to the window edge when the browser window is smaller than the popup', async () => {
    vi.stubGlobal('screenX', 40)
    vi.stubGlobal('screenY', 30)
    vi.stubGlobal('outerWidth', 300)
    vi.stubGlobal('outerHeight', 400)
    const { centeredPopupFeatures } = await loadModule()

    expect(centeredPopupFeatures()).toBe('width=500,height=650,left=40,top=30')
  })

  it('stays on a secondary monitor left of the primary one (negative coordinates)', async () => {
    vi.stubGlobal('screenX', -1920)
    vi.stubGlobal('screenY', 0)
    vi.stubGlobal('outerWidth', 1920)
    vi.stubGlobal('outerHeight', 1050)
    const { centeredPopupFeatures } = await loadModule()

    expect(centeredPopupFeatures()).toBe('width=500,height=650,left=-1210,top=200')
  })

  it('focuses the existing popup instead of opening a second one', async () => {
    const { openLoginPopup } = await loadModule()

    const first = openLoginPopup(() => {})
    const second = openLoginPopup(() => {})

    expect(openSpy).toHaveBeenCalledTimes(1)
    expect(popup.focus).toHaveBeenCalledTimes(1)
    expect(second).toBe(first)
  })
})
