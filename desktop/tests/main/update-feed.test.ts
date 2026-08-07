import { describe, expect, it, vi } from 'vitest'
import { configureE2EUpdateFeed } from '../../src/main/update-feed'

describe('E2E update feed override', () => {
  it('accepts only an explicit loopback HTTP feed in E2E mode', () => {
    const updater = { setFeedURL: vi.fn() }

    expect(configureE2EUpdateFeed(updater, true, 'http://127.0.0.1:19400/updates/')).toBe(true)
    expect(updater.setFeedURL).toHaveBeenCalledWith({
      provider: 'generic',
      url: 'http://127.0.0.1:19400/updates/'
    })
  })

  it.each([
    [false, 'http://127.0.0.1:19400/updates/'],
    [true, 'https://example.com/updates/'],
    [true, 'file:///C:/updates/'],
    [true, 'not-a-url'],
    [true, undefined]
  ])('rejects non-test or non-loopback feeds', (isE2E, url) => {
    const updater = { setFeedURL: vi.fn() }

    expect(configureE2EUpdateFeed(updater, isE2E, url)).toBe(false)
    expect(updater.setFeedURL).not.toHaveBeenCalled()
  })
})
