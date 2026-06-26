# Codex Account Switcher

[Русский](docs/ru/README.md) | [English](docs/en/README.md) | [中文](docs/zh/README.md)

Codex Account Switcher is a local Windows utility for people who use several Codex/OpenAI accounts on the same computer and want switching to feel predictable instead of fragile.

Codex keeps the current sign-in in `auth.json`. When you sign out in the usual way, refresh tokens can be revoked, which may break a saved session and force another manual login. This app takes a safer approach: it keeps separate local sign-in snapshots for your profiles, switches only `auth.json`, and leaves shared Codex state alone.

That means your chats, projects, MCP servers, plugins, skills, tool settings, and local Codex workspace stay common, while the active account can be changed from a small desktop app.

## Search / Discoverability

This project is also useful for people searching for: Codex account switcher, Codex profile switcher, switch Codex accounts on Windows, OpenAI account switcher for Codex, Codex limits viewer, Codex multi-account manager, and safe Codex `auth.json` switcher.

Russian search phrases: переключатель аккаунтов Codex, смена аккаунта Codex, Codex свитчер, переключение профилей Codex, лимиты Codex, несколько аккаунтов Codex на Windows.

Chinese search phrases: Codex账号切换器, Codex账户切换, Codex多账号管理, OpenAI账号切换工具, Windows Codex账号管理, Codex限额查看.

## Download

Use the latest release assets from [GitHub Releases](https://github.com/korsun009/codex-account-switcher/releases/latest):

| System | Installer | Portable |
| --- | --- | --- |
| Windows 10/11 x64 | `CodexAccountSwitcherSetup-win-x64.msi` | `CodexAccountSwitcher-portable-win-x64.zip` |
| Windows 10/11 x86 | `CodexAccountSwitcherSetup-win-x86.msi` | `CodexAccountSwitcher-portable-win-x86.zip` |

Windows 8/8.1 are not targeted. The app is self-contained; users do not need to install .NET separately.

## Main Use Cases

- Keep work, personal, and test Codex accounts on one Windows machine.
- Switch between profiles without using the normal logout flow.
- Add new Codex profiles through a guided flow inside the app.
- See Codex usage limits for all saved profiles from one place.
- Keep one shared `.codex` home for projects, chats, MCP, plugins, and tools.

## What The App Does

- Finds the local `.codex` folder automatically on first launch, with manual selection if needed.
- Stores the list of profiles in a local SQLite database.
- Stores each profile sign-in as a separate local `auth.json` snapshot.
- Makes a backup before replacing the live sign-in.
- Provides x64 and x86 portable builds.
- Provides x64 and x86 MSI installers with normal Windows uninstall support.
- Supports Russian, English, and Chinese in the app UI.

## What The App Does Not Do

- It does not upload tokens or account data anywhere.
- It does not write `auth.json` contents into the SQLite database.
- It does not separate chats, projects, MCP, plugins, skills, or tools by account.
- It does not bypass OpenAI limits, subscriptions, account checks, or access rules.
- It does not support Windows 8/8.1 as a release target.

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

## Codex Limits

The app includes a Codex limits screen. It checks the 5-hour and weekly usage state for saved profiles and displays the result inside the app. The check uses the locally saved sign-in for each profile, does not print token values, and does not store bearer tokens in the database.

This feature is informational. It does not change limits and does not attempt to work around them.

## Remote API

The app can also run an optional local HTTP API with `CodexAccountSwitcher.exe --remote-api`.
It exposes fixed operations for status, account switching, limits, Codex process control, selected Windows power actions, and V2RayTun helpers.

The API is designed for local or private-gateway use. It requires a bearer token for every route except `/health`, does not return raw `auth.json` or token values, and should not be exposed directly to the public internet.

See [docs/REMOTE_API.md](docs/REMOTE_API.md) for routes, environment variables, and scheduled-task setup.

## Interface

- Theme options: automatic, dark, gray, and light.
- Automatic mode follows the Windows app theme.
- The darkest theme is used when Windows is in dark mode.
- Language options: Russian, English, and Chinese.
- Profiles can be added and removed from the Settings screen.

## Build From Source

Requirements:

- Windows 10/11
- .NET 8 SDK
- WiX Toolset support for the installer projects

Run:

```powershell
dotnet test codex-account-switcher\CodexAccountSwitcher.sln
.\scripts\build-release.ps1 -Version v1.0.1
```

The build script creates the same four Windows variants used in public releases.
