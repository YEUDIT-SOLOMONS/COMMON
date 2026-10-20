import { useEffect, useState } from 'react'
import {
  subscribeWelcome,
  WELCOME_FADE_MS,
  WELCOME_GREETING_MS,
  WELCOME_NAVIGATE_MS,
} from '../utils/welcomeTransition.js'
import { BrandMark } from './BrandMark.jsx'

// מסך המעבר אחרי התחברות (ר' utils/welcomeTransition.js): שלושה שלבים - ברכה ('in'), העיגול הוורוד מתרחב
// וממלא את המסך ('grow'), והמסך נמוג וחושף את העמוד החדש ('out')
export function WelcomeTransition() {
  const [state, setState] = useState(null) // { name, phase, run }

  useEffect(
    () =>
      subscribeWelcome((name) => {
        setState((prev) => ({ name, phase: 'in', run: (prev?.run ?? 0) + 1 }))
      }),
    [],
  )

  const run = state?.run
  useEffect(() => {
    if (!run) return undefined
    const setPhase = (phase) => setState((s) => (s && s.run === run ? { ...s, phase } : s))
    const timers = [
      window.setTimeout(() => setPhase('grow'), WELCOME_GREETING_MS),
      window.setTimeout(() => setPhase('out'), WELCOME_NAVIGATE_MS + 60),
      window.setTimeout(() => setState((s) => (s && s.run === run ? null : s)), WELCOME_NAVIGATE_MS + 60 + WELCOME_FADE_MS),
    ]
    return () => timers.forEach((t) => window.clearTimeout(t))
  }, [run])

  if (!state) return null

  const firstName = (state.name ?? '').trim().split(/\s+/)[0]

  return (
    <div className={`welcome-transition welcome-${state.phase}`} role="status" aria-live="polite">
      <div className="welcome-stage">
        <BrandMark className="welcome-mark" />
        <p className="welcome-greeting">{firstName ? `ברוכה הבאה, ${firstName}` : 'ברוכה הבאה'}</p>
      </div>
    </div>
  )
}
