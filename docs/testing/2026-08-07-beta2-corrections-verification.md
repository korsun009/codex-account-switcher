# Electron V2 beta.2 corrections verification

Date: 2026-08-07

## Requirement cross-check

| Source requirement | Implemented evidence |
|---|---|
| Arbitrary account names and existing profiles | New profiles use opaque storage IDs while display names accept normalized printable Unicode. Legacy safe Windows path segments remain supported; traversal, reserved devices and unsafe paths remain rejected. |
| Project logo | The supplied bitmap is packaged as `desktop/src/renderer/src/assets/logo.png` and rendered in the sidebar. |
| Public Remote section | Previous deployment-specific wording was removed. Users can create, list, test and delete generic HTTPS/loopback connections in the GUI; tokens are protected with DPAPI and are never returned to the renderer. |
| Telegram integration | `integrations/telegram-bot/` contains a Russian button UI, current fixed Remote API routes, confirmation for power actions and environment-only deployment settings. |
| In-app updates | Settings checks the fixed official GitHub prerelease channel and opens the matching release. Automatic download/install is deliberately disabled while Windows artifacts are unsigned. User state stays outside the install directory. |
| Five-hour limits | Renderer tests cover five-hour-only, weekly-only and combined windows, including missing reset time and boundary percentages. Backend classifies windows by duration, including 18,000 seconds. |
| Working language selector | RU, EN and ZH use one `translations.json`; key parity and immediate switching are tested in both component and installed-app tests. |
| Light theme | Semantic variables cover surfaces, controls, progress tracks, pills, notices and focus/status states. Installed screenshots were checked at 1360x820, 1100x700 and 980x660. |
| Button alignment | Shared button, icon and status-pill rules center text/icons with stable line height and nonshrinking icons. |
| Cyrillic encoding | Electron/.NET bridge explicitly uses UTF-8. Process-level tests cover Cyrillic, emoji, Chinese and diacritics; conservative repair migrates known persisted mojibake. |

## Fresh verification

- .NET: 81 passed, 0 failed.
- Electron TypeScript: passed.
- Electron unit/component: 22 passed, 0 failed.
- Installed NSIS E2E: 3 passed, including sandbox metadata boundary, all primary pages, RU/EN/ZH and light-theme screenshots.
- Installed backend product version: `2.0.0-beta.2`.
- NSIS upgrade `2.0.0-beta.1 -> 2.0.0-beta.2`: passed; user-data sentinel preserved.
- Telegram template: 3 passed, 0 failed.
- Release secret audit: Git history, current source, installer and unpacked artifact passed.

## Artifact

- `desktop/release/Codex-Account-Switcher-2.0.0-beta.2-x64-Setup.exe`
- SHA-256: `29008E441444D74F241B05247AC24DDACDD28DE5C84386D918388E4FAB9A48C3`
- Authenticode: `NotSigned`
- Published: no

The product/release family remains `2.0.0`; only the prerelease suffix changed to `beta.2`. A real account switch was not performed because it would close the active Codex process; fake-home, UTF-8, backup and rollback paths are covered by tests.
