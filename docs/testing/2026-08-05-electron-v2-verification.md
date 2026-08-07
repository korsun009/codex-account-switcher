# Electron V2 Verification - 2026-08-05

## Result

The local x64 beta installer is ready for user acceptance testing. It has not been published to GitHub.

## Automated Evidence

| Check | Result |
|---|---|
| .NET backend | 54/54 passed |
| Electron unit/component tests | 9/9 passed |
| TypeScript | passed |
| Installed packaged Electron E2E | 2/2 passed |
| Telegram bot authorization | 2/2 passed |
| Git history and artifact secret audit | passed; 0 leaks |

The artifact audit covered 27 Git commits, the unpacked app and the installed app directory. It rejects credential filenames such as `auth.json`, `.env`, profile vaults, switcher databases and private keys.

## Behavioral Evidence

- A clean database returns no hardcoded accounts.
- Profile snapshots are protected with Windows DPAPI CurrentUser and migrated away from plaintext profile `auth.json` files.
- Active and last profiles can be deleted without deleting the live Codex login.
- Switching validates the target, backs up the live login, writes atomically and rolls back on failure.
- Codex process control targets the exact `OpenAI.Codex` package tree and exact AUMID; generic CLI, VS Code and ChatGPT Classic processes are excluded.
- Active-profile limits use the fresh live login. Five-hour and weekly windows are classified by duration instead of array order.
- The installed renderer is sandboxed, has no Node `process`/`require`, receives metadata only and shows no credential values.
- The installed Remote page reports the configured Windows API, backend `2.0.0.0`, one Codex shell, no renderer errors and no horizontal overflow.
- Windows Remote API health, accounts, limits and power route contracts were checked through an isolated test gateway.
- `CodexStartRemoteApi` now remains `Running` in Task Scheduler because both wrappers wait for the tracked process. HTTP.sys points to the stable `%APPDATA%\CodexAccountSwitcher\runtime` executable.

## Artifact

- File: `desktop/release/Codex-Account-Switcher-2.0.0-beta.1-x64-Setup.exe`
- SHA256: `1A0A22C7EDD3C4EEAF71B1CD544F43183D3A00401B79FADB1B35B93BA1DEEC66`
- Authenticode: `NotSigned`

## Deliberate User-Acceptance Gates

- A real account switch was not run while this Codex task was active because it would intentionally close the current Codex package tree. The fake-home, rollback and exact-process tests passed; the user should perform the final live switch from the installed beta.
- The legacy v1.0.1 MSI was not installed over the user's existing production installation. MSI-to-NSIS migration should be checked in an isolated VM after beta acceptance; the provided upgrade harness covers NSIS-to-NSIS data preservation.
- The current handoff is x64 only. x86 packaging remains a release-time compatibility check, not part of this beta handoff.
- Public README translations, commit, push and GitHub release remain blocked until user approval.
