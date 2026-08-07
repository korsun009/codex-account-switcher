import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const css = readFileSync(resolve(process.cwd(), 'src/renderer/src/styles.css'), 'utf8')

describe('renderer theme CSS', () => {
  it('defines semantic light-theme tokens instead of component-specific dark leftovers', () => {
    expect(css).toMatch(/:root\[data-theme=["']light["']\]\s*\{[^}]*--surface:/s)
    expect(css).toMatch(/\.status-pill\s*\{[^}]*background:\s*var\(--status-neutral-bg\)/s)
    expect(css).toMatch(/\.limit-meter__track\s*\{[^}]*background:\s*var\(--progress-track\)/s)
    expect(css).toMatch(/\.notice-banner\s*\{[^}]*background:\s*var\(--notice-bg\)/s)
    expect(css).toMatch(/\.error-banner\s*\{[^}]*background:\s*var\(--error-bg\)/s)
  })

  it('centers button and status-pill contents with explicit line height', () => {
    expect(css).toMatch(/\.button,\s*\.icon-button\s*\{[^}]*justify-content:\s*center[^}]*line-height:\s*1/s)
    expect(css).toMatch(/\.status-pill\s*\{[^}]*justify-content:\s*center[^}]*line-height:\s*1/s)
  })

  it('does not let row typography override the status-pill flex layout', () => {
    expect(css).not.toMatch(/\.setting-row strong,\s*\.setting-row span/)
    expect(css).not.toMatch(/\.backup-row strong,\s*\.backup-row span/)
    expect(css).not.toMatch(/\.connection-row strong,\s*\.connection-row span/)
    expect(css).toMatch(/\.backup-row\s*>\s*div\s*>\s*span\s*\{[^}]*display:\s*block/s)
  })
})
