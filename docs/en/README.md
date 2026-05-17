# Codex Account Switcher

## What it is

Codex Account Switcher is a local Windows app for safely switching between several Codex/OpenAI accounts on the same computer.

It is useful when you have work, personal, or separate Codex accounts but want projects, chats, MCP, plugins, skills, and tool settings to remain shared.

## The problem it solves

A normal Codex logout can revoke the refresh token. After that, a saved sign-in can stop working and require manual login again. This app does not use normal logout. It switches only the account sign-in file, `auth.json`, and leaves shared Codex state untouched.

## Download

GitHub Releases provide four main files:

| System | Installer | Portable |
| --- | --- | --- |
| Windows 10/11 x64 | `CodexAccountSwitcherSetup-win-x64.msi` | `CodexAccountSwitcher-portable-win-x64.zip` |
| Windows 10/11 x86 | `CodexAccountSwitcherSetup-win-x86.msi` | `CodexAccountSwitcher-portable-win-x86.zip` |

Windows 8/8.1 are not supported. .NET does not need to be installed separately.

## How to use

1. Install the MSI or unpack the portable zip.
2. Start the app.
3. On first launch, let it find `.codex`; if it cannot, select the folder manually.
4. Click `Add account`.
5. Enter a clear profile name.
6. Click `Open Codex`, then sign in manually to the needed account.
7. Return to the app and click `Save sign-in`.
8. Use `Switch` on a profile card to change accounts.

## Safety

- Only `auth.json` is switched.
- Projects, chats, MCP, plugins, and tools stay shared.
- `auth.json` contents are not stored in the SQLite database.
- A backup is created before replacing the live sign-in.
- Tokens are not printed in the UI or log.

## Codex limits

The `Codex Limits` screen shows the 5-hour and weekly limits for all saved profiles. The app reads only the required values from `auth.json`, calls the usage endpoint, and does not store tokens.
