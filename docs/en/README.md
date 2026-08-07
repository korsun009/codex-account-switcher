# Codex Account Switcher for Windows

[Main README](../../README.md) | [Русский](../ru/README.md) | [中文](../zh/README.md)

Codex Account Switcher is an open-source Windows app for switching multiple OpenAI Codex accounts, viewing 5-hour and weekly limits, and optionally controlling Codex through Telegram. Chats, projects, MCP servers, plugins, skills, and settings stay shared; only the active sign-in changes.

## Download 2.0.0

Get the release from [GitHub Releases](https://github.com/korsun009/codex-account-switcher/releases/latest) and verify `SHA256SUMS.txt`.

| Windows | Auto-update installer | Architecture installer | MSI | Portable |
| --- | --- | --- | --- | --- |
| 10/11 x64 | `Codex-Account-Switcher-2.0.0-Setup.exe` | `Codex-Account-Switcher-2.0.0-x64-Setup.exe` | `Codex-Account-Switcher-2.0.0-x64-Setup.msi` | `Codex-Account-Switcher-2.0.0-x64-Portable.zip` |
| 10/11 x86 | `Codex-Account-Switcher-2.0.0-Setup.exe` | `Codex-Account-Switcher-2.0.0-ia32-Setup.exe` | `Codex-Account-Switcher-2.0.0-ia32-Setup.msi` | `Codex-Account-Switcher-2.0.0-ia32-Portable.zip` |

`ia32` means 32-bit x86 Windows. Builds are self-contained. macOS, Linux, Windows 8, and Windows 8.1 are unsupported.

> v2.0.0 is not Authenticode signed. Windows can show an unknown-publisher or SmartScreen warning. Checksums verify integrity but do not replace a trusted publisher signature.

## Highlights

- GUI profile creation, removal, and switching.
- DPAPI CurrentUser encryption for saved sign-ins and backups.
- `auth.json` validation, backup, and rollback.
- 5-hour and weekly Codex usage limits without token output.
- Explicit check, download, and install update actions.
- Russian, English, and Chinese UI plus four themes.
- Optional authenticated Remote API and button-first Telegram bot.

## Use

Install the recommended NSIS EXE, MSI, or portable ZIP. Add an account in the guided flow, open Codex, sign in manually, then return and save that sign-in. Use **Switch** on profile cards. Avoid normal Codex logout between saved profiles because it can revoke refresh tokens.

The NSIS EXE is recommended for in-app updates. MSI and portable users can check the version in the app and update manually from GitHub Releases.

## Security

The app switches only the live `auth.json`. Saved sign-ins are encrypted as `auth.dpapi` with Windows DPAPI for the current Windows user. They are intentionally not portable to another PC or Windows user. A synchronized profile name can appear elsewhere, but its encrypted credentials cannot be decrypted there and must be recorded again.

Tokens are not stored in SQLite, printed in the UI, or returned by the Remote API. The app does not bypass OpenAI limits, subscriptions, or access controls.

## Telegram and translations

See the [GUI and Telegram setup guide](../TELEGRAM_GUI_SETUP.md), [Remote API](../REMOTE_API.md), and the separate [codex-telegram-remote](https://github.com/korsun009/codex-telegram-remote) deployment repository.

Community translations are welcome through a fork and pull request. Edit [`desktop/src/renderer/src/locales/translations.json`](../../desktop/src/renderer/src/locales/translations.json), preserve the key tree and `{{placeholders}}`, validate, and open a PR. See [TRANSLATIONS.md](../TRANSLATIONS.md).

Build and contribution details are in the [main README](../../README.md), [CONTRIBUTING.md](../../CONTRIBUTING.md), and [v2.0.0 release notes](../RELEASE_NOTES_2.0.0.md).
