# Codex Account Switcher V2 Final GitHub Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish a complete, searchable GitHub `v2.0.0` release with verified Windows x64/x86 artifacts, working in-app updates, current Telegram integration documentation, and a community translation workflow.

**Architecture:** Electron V2 remains a Windows-only desktop client with a self-contained .NET backend. NSIS is the canonical updater target; MSI and portable ZIP variants are additional installation choices. `codex-account-switcher` remains the canonical application/API repository, while `codex-telegram-remote` remains the deployment repository for the Telegram bot and always-on gateway and is synchronized to the V2 API contract.

**Tech Stack:** Electron 43, React 19, TypeScript 7, electron-builder 26, .NET 8 Windows, NSIS, WiX/MSI, Python 3.12, python-telegram-bot, GitHub CLI/API, Gitleaks.

---

### Task 1: Prove the updater path

**Files:**
- Create: `desktop/src/main/update-feed.ts`
- Create: `desktop/tests/main/update-feed.test.ts`
- Create: `desktop/tests/e2e/update-feed-server.mjs`
- Create: `scripts/test-updater-feed.ps1`
- Modify: `desktop/src/main/index.ts`
- Modify: `desktop/tests/e2e/packaged-app.spec.ts`

- [x] Add a loopback-only E2E feed override guarded by `CODEX_SWITCHER_E2E=1`.
- [x] Reject remote, file, malformed, and non-E2E feed overrides.
- [x] Serve a generated `latest.yml`, blockmap, and real installer on loopback.
- [x] Prove packaged `2.0.0` reports `2.0.1` as available and downloads it with SHA-512 verification.
- [x] Record that the public beta failed because GitHub Release `v1.0.1` has neither `beta.yml` nor `latest.yml`.

### Task 2: Build every supported Windows variant

**Files:**
- Modify: `desktop/package.json`
- Modify: `scripts/build-release.ps1`
- Modify: `desktop/tests/main/package-config.test.ts`

- [x] Publish the backend for `win-x64` and `win-x86` into architecture-mapped resource directories.
- [x] Build NSIS x64 and ia32 installers in one metadata-producing pass.
- [x] Build MSI x64 and ia32 installers for managed enterprise/manual deployment.
- [x] Build unpacked x64 and ia32 directories and archive them as portable ZIP files.
- [x] Generate `SHA256SUMS.txt` covering every uploaded binary and metadata file.
- [x] Keep `appId`, NSIS GUID, user-data path, and uninstall retention policy unchanged.
- [x] Verify all artifacts are honest about Authenticode status; do not claim signing without a certificate.

### Task 3: Synchronize Telegram remote control

**Files:**
- Modify: `integrations/telegram-bot/app.py`
- Modify: `integrations/telegram-bot/bot_core.py`
- Modify: `integrations/telegram-bot/test_bot_core.py`
- Create: `docs/TELEGRAM_GUI_SETUP.md`
- Update in separate repository: `codex-telegram-remote/bot/*`, `README.md`

- [x] Keep button-first Russian controls for status, accounts, limits, Codex, VPN, Wake-on-LAN, sleep, reboot, and shutdown.
- [x] Add dynamic account buttons and POST `/switch-account` using current display names.
- [x] Format 5-hour and weekly limits without exposing token/error internals.
- [x] Require one-time confirmation for sleep, reboot, and shutdown.
- [x] Document creation of Remote connections from the desktop GUI without personal infrastructure defaults.
- [x] Keep the always-on gateway/reverse-tunnel deployment in `codex-telegram-remote` and link it from the main repository.
- [x] Run Python tests and a secret/personal-marker audit in both repositories.

### Task 4: Rewrite public documentation and translation workflow

**Files:**
- Modify: `README.md`
- Modify: `docs/ru/README.md`
- Modify: `docs/en/README.md`
- Modify: `docs/zh/README.md`
- Create: `CONTRIBUTING.md`
- Create: `docs/TRANSLATIONS.md`
- Create: `docs/RELEASE_NOTES_2.0.0.md`

- [x] Replace V1 screenshots/artifact names and instructions with the Electron V2 workflow.
- [x] Document supported Windows versions, x64/x86 choices, installer/portable differences, upgrade and uninstall behavior.
- [x] Document DPAPI CurrentUser portability limits and `auth.json` safety boundaries.
- [x] Explain how to fork, edit `desktop/src/renderer/src/locales/translations.json`, validate all keys, and open a pull request.
- [x] List all V2 changes, Telegram capabilities, update behavior, security properties, and known unsigned-build limitation.
- [x] Keep Russian, English, and Chinese entry points internally consistent.

### Task 5: GitHub discoverability and repository metadata

**Files:**
- Modify: `README.md`
- Modify through GitHub API: repository description, homepage, topics.

- [x] Put the literal product/category and Windows/Codex keywords in the README title and first paragraph.
- [x] Add concise feature, security, installation, Telegram, translation, and troubleshooting sections.
- [x] Add release/build/license/platform badges with stable alt text.
- [x] Set a useful repository description and topics such as `codex`, `openai`, `account-switcher`, `electron`, `windows`, `telegram-bot`, `dpapi`, and `remote-control`.
- [x] Verify public README links and release asset links without relying on authenticated browser state.

### Task 6: Publish and verify GitHub v2.0.0

**Files:**
- Git refs: `main`, tag `v2.0.0`
- GitHub Release assets: all installers, portable ZIPs, `latest.yml`, blockmap, `SHA256SUMS.txt`

- [x] Run .NET, Electron, packaged E2E, updater E2E, Telegram, and secret-audit gates.
- [x] Confirm no token, `.env`, `auth.json`, profile database, private host, MAC, or personal default account is tracked or packaged.
- [x] Commit the complete V2 change set with a release-focused message.
- [x] Push `feature/electron-v2` and fast-forward `main` only if remote history is still compatible.
- [x] Create annotated tag `v2.0.0` and a non-draft public GitHub release from the prepared release notes.
- [x] Upload all assets and verify `releases/latest/download/latest.yml` returns HTTP 200.
- [x] Verify the GitHub API reports the expected tag, assets, checksums, topics, and repository description.
- [x] Leave an explicit record that artifacts are `NotSigned` until a trusted Code Signing certificate is configured.

## Self-review

- Spec coverage: updater, all Windows variants, Telegram repository, GUI instructions, translations, release notes, GitHub SEO, commit/push/tag/release, and public verification are covered.
- Placeholder scan: no implementation placeholder is used as a release step; each unchecked item names a concrete output and verification.
- Type consistency: updater uses `latest`/`latest.yml`; x86 is electron-builder `ia32` and .NET `win-x86`; the Telegram bot targets the documented V2 routes.
