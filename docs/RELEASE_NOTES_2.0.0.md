# Codex Account Switcher 2.0.0

Version 2.0.0 is a full Electron desktop rewrite with a hardened .NET backend, encrypted profile credentials, limits, updates, remote connections, and a current Telegram control plane.

## Highlights

- New Windows interface with Russian, English, and Chinese localization.
- Automatic, dark, gray, and light themes.
- Guided account creation and reliable switching of only the live `auth.json`.
- Windows DPAPI CurrentUser encryption for saved profile sign-ins and credential backups.
- Verified migration from legacy plaintext profile credentials to `auth.dpapi`.
- Backup validation and rollback protection before account replacement.
- Detection, launch, and stop support for current Codex desktop processes.
- 5-hour and weekly limits with reset times for every saved profile.
- Local SQLite storage for non-secret application state.
- Optional authenticated Remote API for accounts, limits, Codex, V2RayTun, power, autologon diagnostics, and firewall setup.
- Telegram buttons for Wake-on-LAN, status, account switching, limits, Codex start/stop, V2RayTun Proxy Mode, sleep, reboot, and shutdown.
- Short-lived one-time confirmations for power actions.
- Stable in-app updater with separate check, download, and install actions.
- Service connection management in the Remote GUI; connection tokens are protected by DPAPI.
- Global status-pill alignment fix and responsive packaged-app geometry tests.

## Packages

The release includes a combined x64/x86 NSIS updater, architecture-specific NSIS EXE, MSI, and portable ZIP variants, plus `latest.yml`, updater blockmaps, and `SHA256SUMS.txt`. NSIS is the canonical in-app update format.

## Security notes

- Tokens, `auth.json`, `auth.dpapi`, profile databases, `.env` files, private hosts, and machine-specific identifiers are excluded from source and release assets.
- Redirects are disabled for bot bearer-token requests.
- Remote API should remain on loopback or a trusted LAN behind a strict gateway/firewall rule.
- DPAPI-encrypted sign-ins are intentionally bound to the same Windows user and are not portable across PCs.
- The app does not bypass OpenAI limits, subscriptions, or access controls.

## Known limitation

The 2.0.0 Windows binaries are **not Authenticode signed** because no trusted Code Signing certificate is currently configured. Windows may display an unknown-publisher or SmartScreen warning. Verify downloads against `SHA256SUMS.txt`. A future release will add a trusted signature; no self-signed certificate is presented as publisher identity.

## Updating from 1.x or beta builds

User data is retained on uninstall and upgrade. The app migrates discovered legacy profile credentials only after an encrypted round-trip verification succeeds. Keep a backup of the Codex Home directory before a major upgrade. NSIS users can update through the explicit in-app flow after 2.0.0 is installed; MSI and portable users update manually.

## Community translations

Translation pull requests are welcome. Fork the repository, edit `desktop/src/renderer/src/locales/translations.json`, preserve keys and placeholders, run `pnpm typecheck` and `pnpm test`, then open a pull request. See `docs/TRANSLATIONS.md`.
