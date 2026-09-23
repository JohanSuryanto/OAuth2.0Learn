// Shared countdown maths (spec 003 R3/R4, spec 004 R7): count from the server-reported seconds using
// performance.now(), so the computer's clock never matters.

export function secondsLeft(total: number, receivedAt: number, now: number): number {
  return Math.max(0, total - Math.floor((now - receivedAt) / 1000))
}

/** H:MM:SS */
export function formatDuration(seconds: number): string {
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = seconds % 60
  return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

/** M:SS, for short countdowns. */
export function formatMinutes(seconds: number): string {
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
}
