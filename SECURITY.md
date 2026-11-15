# Security Policy

## Secrets

Never commit or upload:

- `auth.json`
- OAuth tokens
- bearer tokens
- `.codex\_account_profiles`
- `.codex\_account_switcher_backups`
- local SQLite databases
- exported `.codexprofiles` files (even encrypted)

## Scope

The app switches the saved Codex sign-in and carries local project and chat sidebar metadata between the selected profiles. A layout change creates a CurrentUser DPAPI backup (`sidebar.dpapi`) beside the credential backup. Shared SQLite databases and chat files stay in place. Cloud project references, cloud chats, permissions and drafts stay scoped to their account.

## Reporting issues

Open a GitHub issue and include:

- Windows version and architecture
- app version
- whether you used MSI or portable
- what action failed
- log text from the app, with tokens removed

## Portable profile archives

Profile exports contain saved sign-ins and names, encrypted together with a user-supplied password. Treat the file and password as account-access material and send them over separate channels. There is no password recovery. Exported tokens may expire or be revoked; import does not guarantee that the provider will accept an expired login.

Import stores credentials under the destination Windows user protection and does not activate an account or replace the shared Codex home. Existing profiles remain intact. Chat/project files, remote API settings, backups and chat-state snapshots are outside the archive scope.
