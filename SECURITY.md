# Security Policy

## Secrets

Never commit or upload:

- `auth.json`
- OAuth tokens
- bearer tokens
- `.codex\_account_profiles`
- `.codex\_account_switcher_backups`
- local SQLite databases

## Scope

The app switches the saved Codex sign-in and carries local project and chat sidebar metadata between the selected profiles. A layout change creates a CurrentUser DPAPI backup (`sidebar.dpapi`) beside the credential backup. Shared SQLite databases and chat files stay in place. Cloud project references, cloud chats, permissions and drafts stay scoped to their account.

## Reporting issues

Open a GitHub issue and include:

- Windows version and architecture
- app version
- whether you used MSI or portable
- what action failed
- log text from the app, with tokens removed
