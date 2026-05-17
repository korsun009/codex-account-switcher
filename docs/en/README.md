# Codex Account Switcher

## What it is

Codex Account Switcher is a local Windows app for safely switching between several Codex/OpenAI accounts on the same computer. It is designed for users who want multiple Codex sign-ins without repeatedly logging out, breaking refresh tokens, or rebuilding the local Codex environment for every account.

The app keeps projects, chats, MCP servers, plugins, skills, and tool settings shared. Only the active Codex sign-in changes.

It is useful if you have:

- a work account and a personal account;
- separate accounts for different projects;
- a test account for checking Codex behavior;
- several profiles that should share one local Codex setup.

## The problem it solves

Codex stores the current sign-in in `auth.json`. A normal Codex logout can revoke the refresh token. After that, a saved sign-in can stop working and require manual login again.

Codex Account Switcher does not use the normal logout flow. Instead, it keeps separate local sign-in snapshots for each profile and switches only `auth.json`. Everything else inside `.codex` remains shared.

This gives you quick account switching while keeping one common Codex workspace.

## Download

Download the latest version from [GitHub Releases](https://github.com/korsun009/codex-account-switcher/releases/latest). Releases provide four main files:

| System | Installer | Portable |
| --- | --- | --- |
| Windows 10/11 x64 | `CodexAccountSwitcherSetup-win-x64.msi` | `CodexAccountSwitcher-portable-win-x64.zip` |
| Windows 10/11 x86 | `CodexAccountSwitcherSetup-win-x86.msi` | `CodexAccountSwitcher-portable-win-x86.zip` |

Windows 8/8.1 are not supported. .NET does not need to be installed separately.

## What the app can do

- Automatically locate the `.codex` folder on first launch.
- Ask for manual folder selection if automatic detection fails.
- Add new profiles from the app UI.
- Guide the user through creating a profile, opening Codex, signing in manually, and saving the sign-in.
- Switch the active profile with one button.
- Show the currently active account.
- Show Codex limits for all saved profiles.
- Store the profile list in a local SQLite database.
- Create a backup before replacing the live sign-in.
- Support Russian, English, and Chinese UI languages.
- Support automatic, dark, gray, and light themes.
- Follow the Windows app theme in automatic mode.

## What the app does not do

- It does not upload tokens or account data to any server.
- It does not store `auth.json` contents in the SQLite database.
- It does not separate projects, chats, MCP, plugins, skills, or tools by account.
- It does not bypass OpenAI or Codex limits.
- It does not change subscriptions, account permissions, or access rules.
- It does not support Windows 8/8.1.

## How to use

1. Install the MSI or unpack the portable zip.
2. Start the app.
3. On first launch, let it find `.codex`; if it cannot, select the folder manually.
4. Click `Add account`.
5. Enter a clear profile name.
6. Click `Open Codex`, then sign in manually to the needed account.
7. Return to the app and click `Save sign-in`.
8. Use `Switch` on a profile card to change accounts.

## Adding a new account

Adding an account is handled by a guided flow inside the app. The user stays in control, but does not need to remember a sequence of separate utility buttons.

1. Open `Add account`.
2. Enter a profile name, such as `Work Codex` or `Personal`.
3. Click `Create profile`.
4. Click `Open Codex`.
5. In Codex, sign in manually to the account you want.
6. Return to Codex Account Switcher.
7. Click `Save sign-in`.

The profile will then appear in the profile list and can be used for switching.

## Safety

- Only `auth.json` is switched.
- Projects, chats, MCP, plugins, and tools stay shared.
- `auth.json` contents are not stored in the SQLite database.
- A backup is created before replacing the live sign-in.
- Tokens are not printed in the UI or log.

Important: do not use the normal Codex logout button while recording several accounts. Logout can revoke the refresh token and break a saved sign-in.

## Codex limits

The `Codex Limits` screen shows 5-hour and weekly limits for all saved profiles. The app reads only the required values from each local profile sign-in, calls the usage endpoint, and does not store bearer tokens in the database.

This feature is informational only. It does not increase limits and does not attempt to bypass them.

## Installer and portable builds

There are two ways to use the app:

- MSI installer: normal Windows installation, with removal through Windows settings or the uninstall file.
- Portable zip: unpack the folder and run the app without installation.

Both variants are published for x64 and x86. User-facing changes should be verified across all four release variants.

## Build from source

Requirements: Windows 10/11, .NET 8 SDK, and WiX Toolset support for the installer projects.

```powershell
dotnet test codex-account-switcher\CodexAccountSwitcher.sln
.\scripts\build-release.ps1 -Version v1.0.1
```

The script creates portable x64, portable x86, MSI x64, MSI x86, the source archive, and `SHA256SUMS.txt`.
