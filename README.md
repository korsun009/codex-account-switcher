# Codex Account Switcher

[Русский](docs/ru/README.md) | [English](docs/en/README.md) | [中文](docs/zh/README.md)

Codex Account Switcher is a local Windows utility for people who use several Codex/OpenAI accounts on one computer and want to switch between them without losing shared chats, projects, tools, MCP settings, and local configuration.

## Download

Use the latest release assets:

| System | Installer | Portable |
| --- | --- | --- |
| Windows 10/11 x64 | `CodexAccountSwitcherSetup-win-x64.msi` | `CodexAccountSwitcher-portable-win-x64.zip` |
| Windows 10/11 x86 | `CodexAccountSwitcherSetup-win-x86.msi` | `CodexAccountSwitcher-portable-win-x86.zip` |

Windows 8/8.1 are not targeted. The app is self-contained; users do not need to install .NET separately.

## What problem it solves

Codex stores its active OpenAI sign-in in `auth.json`. If you work with multiple Codex accounts, manually logging out and signing in can revoke refresh tokens and break saved sessions. This app keeps separate safe profile snapshots of `auth.json` while leaving shared Codex state untouched.

## Safety model

- Switches only `auth.json`.
- Keeps chats, projects, sessions, MCP, plugins, skills, and tools shared.
- Never stores `auth.json` contents in the SQLite database.
- Creates backups before replacing the live sign-in.
- Shows Codex usage limits for all saved profiles without logging tokens.

## Quick start

1. Install the MSI or unpack the portable zip.
2. Open Codex Account Switcher.
3. Let it find your `.codex` folder, or select it manually.
4. Click **Add account**.
5. Create a profile, open Codex for sign-in, sign in manually, then save the sign-in.
6. Use **Switch** on profile cards to move between accounts.

## Languages

The app UI supports Russian, English, and Chinese. Change language in **Menu -> Settings**.

## Release structure

Every release should publish these assets:

- `CodexAccountSwitcherSetup-win-x64.msi`
- `CodexAccountSwitcherSetup-win-x86.msi`
- `CodexAccountSwitcher-portable-win-x64.zip`
- `CodexAccountSwitcher-portable-win-x86.zip`
- `CodexAccountSwitcher-source-vX.Y.Z.zip`
- `SHA256SUMS.txt`

See `docs/RELEASE_PROCESS.md` for the update checklist.
