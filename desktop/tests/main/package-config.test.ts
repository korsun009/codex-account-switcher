import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

interface PackageConfig {
  version: string
  scripts: { 'package:dir': string; 'package:win': string }
  build: {
    appId: string
    nsis: { guid: string; deleteAppDataOnUninstall: boolean }
    publish: Array<{ provider: string; owner: string; repo: string; channel: string; releaseType: string }>
    extraResources: Array<{ from: string; to: string }>
    win: {
      signtoolOptions: { signingHashAlgorithms: string[]; rfc3161TimeStampServer: string }
      target: Array<{ target: string; arch: string[] }>
    }
  }
}

describe('release configuration', () => {
  it('keeps the established installer identity for the stable release', () => {
    const config = JSON.parse(readFileSync(join(process.cwd(), 'package.json'), 'utf8')) as PackageConfig

    expect(config.version).toBe('2.0.0')
    expect(config.build.appId).toBe('app.codexaccountswitcher.desktop')
    // UUID v5 generated for the first Electron installer. Never change after publication.
    expect(config.build.nsis.guid).toBe('46496e38-c773-566c-b0c4-a8505306ddae')
    expect(config.build.nsis.deleteAppDataOnUninstall).toBe(false)
    expect(config.build.win.signtoolOptions.signingHashAlgorithms).toEqual(['sha256'])
    expect(config.build.win.signtoolOptions.rfc3161TimeStampServer).toBe('http://timestamp.digicert.com')
    expect(config.build.publish).toEqual([
      expect.objectContaining({ provider: 'github', channel: 'latest', releaseType: 'release' })
    ])
  })

  it('packages the architecture-matched backend for every supported Windows format', () => {
    const config = JSON.parse(readFileSync(join(process.cwd(), 'package.json'), 'utf8')) as PackageConfig

    expect(config.build.extraResources).toContainEqual({
      from: '../artifacts/backend/${arch}',
      to: 'backend',
      filter: ['**/*']
    })
    expect(config.build.win.target).toEqual([
      { target: 'nsis', arch: ['x64', 'ia32'] },
      { target: 'msi', arch: ['x64', 'ia32'] }
    ])
    expect(config.scripts['package:dir']).toContain('--x64 --ia32')
    expect(config.scripts['package:win']).toContain('--win nsis msi --x64 --ia32')
  })
})
