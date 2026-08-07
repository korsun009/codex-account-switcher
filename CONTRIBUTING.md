# Contributing

Contributions to Codex Account Switcher are welcome through GitHub pull requests.

## Before opening a pull request

1. Fork `korsun009/codex-account-switcher` and create a focused branch.
2. Do not commit `.env`, `auth.json`, `auth.dpapi`, SQLite databases, logs, tokens, private hostnames, account IDs, or generated release files.
3. Keep `C:\Users\<user>\.codex` as one shared Codex Home. Account switching must remain limited to the active sign-in unless an evidence-backed design explicitly changes that contract.
4. Preserve the Electron `appId`, NSIS GUID, data directory, and uninstall retention behavior.
5. Add or update tests for behavioral changes.

## Validation

```powershell
dotnet test .\codex-account-switcher\CodexAccountSwitcher.sln --configuration Release
Push-Location .\desktop
pnpm install --frozen-lockfile
pnpm typecheck
pnpm test
Pop-Location
```

Release-facing changes must be checked for x64 and x86 (ia32), including NSIS, MSI, and portable layouts. A normal production build also requires valid Authenticode signatures.

## Translations

Translation-only pull requests are accepted. Follow [docs/TRANSLATIONS.md](docs/TRANSLATIONS.md) and avoid unrelated formatting or source changes.

## Security reports

Do not open a public issue containing credentials or a reproducible credential leak. Follow [SECURITY.md](SECURITY.md).
