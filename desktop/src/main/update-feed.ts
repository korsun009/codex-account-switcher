export interface FeedConfigurableUpdater {
  setFeedURL(options: { provider: 'generic'; url: string }): void
}

export function configureE2EUpdateFeed(
  updater: FeedConfigurableUpdater,
  isE2E: boolean,
  candidateUrl: string | undefined
): boolean {
  if (!isE2E || !candidateUrl) {
    return false
  }

  try {
    const url = new URL(candidateUrl)
    if (url.protocol !== 'http:' || (url.hostname !== '127.0.0.1' && url.hostname !== 'localhost')) {
      return false
    }
    updater.setFeedURL({ provider: 'generic', url: url.toString() })
    return true
  } catch {
    return false
  }
}
