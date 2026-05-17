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

The app is intentionally conservative. It switches only the Codex sign-in file `auth.json`; shared chats, projects, MCP, plugins, skills, and tools are not copied or separated.

## Reporting issues

Open a GitHub issue and include:

- Windows version and architecture
- app version
- whether you used MSI or portable
- what action failed
- log text from the app, with tokens removed
