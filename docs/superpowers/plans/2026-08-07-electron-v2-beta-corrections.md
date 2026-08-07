# Electron V2 Beta Corrections ExecPlan

**Status (2026-08-07):** Implemented and verified locally as `2.0.0-beta.2`. No commit, push, or GitHub release was performed.

> **For Codex:** Execute this plan in the existing `feature/electron-v2` worktree. Preserve all pre-existing uncommitted V2 work and do not publish a GitHub release without the user's explicit approval.

**Goal:** Implement every correction from `docs/Правки приложения от 07.08.md`, keep the product version at `2.0.0`, and advance only the prerelease suffix from `beta.1` to `beta.2`.

**Architecture:** Keep Electron as the desktop shell and the .NET executable as the privileged/local backend. Store user state outside the installation directory, keep `C:\Users\<user>\.codex` shared, and never expose credential contents to Electron. Make UI localization, updater state, and integration configuration explicit typed contracts instead of ad hoc strings.

**Stack:** Electron 43, React 19, TypeScript, electron-vite, Vitest, Playwright, .NET 8, Microsoft.Data.Sqlite, DPAPI, electron-builder/NSIS, electron-updater.

---

## Task 1: Profile compatibility and UTF-8 transport

**Files:**
- Modify: `codex-account-switcher/Core/PathSafety.cs`
- Modify: `codex-account-switcher/Core/AccountSwitcherService.cs`
- Modify: `codex-account-switcher/Core/SqliteAppDatabase.cs`
- Modify: `codex-account-switcher/Program.cs`
- Modify: `desktop/src/main/backend-client.ts`
- Test: `codex-account-switcher/CodexAccountSwitcher.Tests/AccountSwitcherServiceTests.cs`
- Test: `codex-account-switcher/CodexAccountSwitcher.Tests/DesktopBridgeTests.cs`
- Test: new process-level UTF-8 and migration tests as appropriate

1. Add failing tests for arbitrary display names and previously valid legacy storage names.
2. Separate user-facing display names from safe internal profile identifiers for newly created profiles.
3. Relax legacy storage-name validation only to valid single Windows path segments; continue rejecting traversal, rooted paths, separators, control characters, reserved device names, and trailing dots/spaces.
4. Force UTF-8 without BOM on the .NET bridge input/output and UTF-8 decoding in Electron.
5. Add a conservative, idempotent repair for already persisted UTF-8/Windows-1251 mojibake. Never rewrite normal text.
6. Verify profile creation, persistence, switching, backup and rollback with Cyrillic, Chinese, diacritics, punctuation and emoji.

## Task 2: One-file localization

**Files:**
- Create: `desktop/src/renderer/src/locales/translations.json`
- Create: `desktop/src/renderer/src/locales/i18n.ts`
- Modify: `desktop/src/renderer/src/App.tsx`
- Modify: `desktop/src/shared/contracts.ts`
- Modify: `desktop/src/preload/index.ts`
- Modify: `desktop/src/main/ipc.ts`
- Test: renderer localization tests

1. Add failing tests that RU, EN and ZH have identical key sets and change visible UI immediately.
2. Move every renderer label, notification, confirmation, status, aria label and page description into one JSON file with `ru`, `en` and `zh` sections.
3. Use a typed translator with Russian fallback and locale-aware date/time formatting.
4. Localize the Codex Home folder dialog title through a bounded IPC argument.
5. Confirm no hardcoded user-facing Cyrillic remains in `.tsx`/renderer TypeScript outside fixtures.

## Task 3: Visual corrections and limits

**Files:**
- Add: square application logo asset under `desktop/src/renderer/src/assets/`
- Modify: `desktop/src/renderer/src/App.tsx`
- Modify: `desktop/src/renderer/src/styles.css`
- Modify: `desktop/src/main/window-security.ts`
- Test: `desktop/tests/e2e/app.spec.ts` and renderer tests

1. Replace the sidebar letter placeholder with the supplied project logo.
2. Replace selective light-theme overrides with semantic CSS color variables for surfaces, borders, text, progress tracks, pills, notices and focus rings.
3. Center text and icons consistently in command buttons and status pills at normal and scaled DPI.
4. Lock limit rendering for five-hour-only, weekly-only and combined data, including 0/100 percent and missing reset timestamps.
5. Capture and inspect dark/light screenshots at 1360x820, 1100x700 and the 980x660 minimum.

## Task 4: Public Remote integrations and Telegram template

**Files:**
- Modify: .NET models/database/desktop bridge contracts and handlers
- Modify: Electron shared contracts and Remote UI
- Create: sanitized `integrations/telegram-bot/` public template and documentation
- Modify: public README/security documentation
- Test: database/bridge/UI tests and Telegram template tests

1. Remove Alice, Finland VPN and other private-infrastructure wording from the product UI and distributable documentation.
2. Add GUI-managed third-party connection profiles with name, type, HTTPS endpoint and a secret token protected by DPAPI. Return only `hasToken`, never the token.
3. Permit plain HTTP only for loopback development; reject unsafe schemes, embedded credentials, overlong values and path-traversal-style inputs.
4. Support create/list/test/delete actions with bounded timeouts and safe error messages.
5. Update the Telegram bot template to current Remote API routes, configure it entirely through environment variables, and keep personal hosts, IDs, tokens, accounts and infrastructure names out of tracked files.
6. Scan the current tree, Git history and distributable artifacts for credential patterns and known personal infrastructure markers.

## Task 5: In-app updates and beta version

**Files:**
- Modify: `desktop/package.json`
- Modify: `desktop/pnpm-lock.yaml`
- Create: `desktop/src/main/update-manager.ts`
- Modify: `desktop/src/main/index.ts`, `desktop/src/main/ipc.ts`, preload/shared contracts
- Modify: Settings UI and translations
- Modify: `codex-account-switcher/codex-account-switcher.csproj`
- Test: updater IPC/unit tests and packaged metadata checks

1. Add `electron-updater` with a fixed HTTPS GitHub Releases provider for `korsun009/codex-account-switcher`; do not embed a GitHub token.
2. Disable automatic download/install. Expose check, download and install actions through fixed IPC channels and a read-only updater state.
3. Run the updater only in a packaged app, enable the beta channel for prereleases, and show useful offline/no-update/error states.
4. Keep application data in `%APPDATA%`/the existing SQLite location and retain `deleteAppDataOnUninstall: false`; add an upgrade test proving data remains.
5. Change package/backend informational version to `2.0.0-beta.2`; keep assembly/file release version `2.0.0.0`.

## Task 6: End-to-end verification and requirement cross-check

**Files:**
- Modify: testing/security reports under `docs/testing/` and `docs/security/`
- Modify: this ExecPlan checkboxes/status as work completes

1. Run focused red-green tests during each task.
2. Run full .NET, Electron unit, typecheck, renderer E2E, Alice and sanitized Telegram tests.
3. Build the x64 backend, Electron ASAR and NSIS `2.0.0-beta.2` installer with update metadata.
4. Run installed-app and beta.1-to-beta.2 upgrade checks without deleting user data.
5. Inspect packaged app screenshots in RU/EN/ZH and dark/light themes.
6. Re-read every item and image reference in `docs/Правки приложения от 07.08.md`; record pass/fail evidence for all nine corrections.
7. Stop before commit, push or release publication unless the user explicitly authorizes them.
