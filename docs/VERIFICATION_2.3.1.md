# 2.3.1 verification

## Diagnosis and regression

On the installed Codex 26.1111.2228.0, the original named-pipe `list_threads` request returned `success:false` / invalid arguments. Read-only comparison isolated the cause: an omitted cursor succeeds, explicit `cursor:null` fails; `archived:false` is accepted. The last-turn read request is also accepted.

The regression assertion failed against the original implementation. After the fix, all 123 backend tests passed, including omitted first-page cursor, subsequent-page cursor, retained unread/error/waiting state and switch-abort safety coverage.

A probe referenced the corrected backend and used a synthetic isolated SQLite database containing only a valid caller ID. Through the installed Codex transport, a complete CaptureAsync succeeded in 4.7 seconds and retained 5 chat states. DPAPI journal write/read roundtrip succeeded in the isolated directory. No live auth/journal replacement, account switch, continuation message, or Codex shutdown was performed.

## Release validation

- Backend: 123 passed; frontend/main IPC: 37 passed across 10 files. TypeScript validation and production build passed.
- Packaged x64 and x86: 6 E2E checks passed per architecture (one unrelated profile-transfer scenario skipped). Accounts -> Settings -> chat snapshots, saved error details, continuation toggle persistence across renderer reload, primary-page navigation, localization/theme and renderer isolation passed with synthetic DPAPI fixtures. The updater-download scenario was excluded without an E2E feed.
- Both architectures used isolated homes and app databases. No installed-app upgrade or live account switch was performed. UI screenshots were inspected.
- Both MSI ProductVersion/platform values, embedded backend version/size and app.asar hashes match the corresponding packaged application. Portable ZIP backend/app.asar hashes match unpacked files; both backend hashes match publish outputs. Source/stage payload hashes match; 11 payload checksums and updater version/path/SHA-512 verified.
- Git history, current source and all requested artifacts passed the secret audit. All four supported distributions were built; NSIS installers/blockmaps and latest.yml are included for updates. Packages remain unsigned as in previous releases, explicitly allowed for this build and documented in the release notes.
- Review: the code change only omits the first-page optional cursor; subsequent-page cursors, pagination-loop detection, encrypted journal persistence and fail-closed capture behavior are unchanged. No schema migration is required for rollback to 2.3.0.
