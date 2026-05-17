# GitHub Upload Structure

Repository name recommended: `codex-account-switcher`

## Initial repository files

Upload the repository root excluding:

- `bin/`
- `obj/`
- `.vs/`
- `release/`
- `chat-backup/`
- any `auth.json`
- any local `.db`, `.db-shm`, `.db-wal`, `.log`

## Release v1.0.1 assets

Attach these files to GitHub Release `v1.0.1`:

- `assets/CodexAccountSwitcherSetup-win-x64.msi`
- `assets/CodexAccountSwitcherSetup-win-x86.msi`
- `assets/CodexAccountSwitcher-portable-win-x64.zip`
- `assets/CodexAccountSwitcher-portable-win-x86.zip`
- `source/CodexAccountSwitcher-source-v1.0.1.zip`
- `SHA256SUMS.txt`

Use `RELEASE_NOTES.md` as the release description.

## Future releases

Run:

```powershell
.\scripts\build-release.ps1 -Version vX.Y.Z
```

Then upload the same stable asset names under `release\vX.Y.Z`.
