# Release Process

This project publishes every user-facing update in four Windows variants:

1. x64 portable zip
2. x86 portable zip
3. x64 MSI installer
4. x86 MSI installer

## Supported systems

- Windows 10/11 x64
- Windows 10/11 x86

Windows 8/8.1 are not release targets.

## Build checklist

Run from the repository root:

```powershell
.\scripts\build-release.ps1 -Version v1.0.1
```

The script creates:

```text
release\vX.Y.Z\assets\CodexAccountSwitcherSetup-win-x64.msi
release\vX.Y.Z\assets\CodexAccountSwitcherSetup-win-x86.msi
release\vX.Y.Z\assets\CodexAccountSwitcher-portable-win-x64.zip
release\vX.Y.Z\assets\CodexAccountSwitcher-portable-win-x86.zip
release\vX.Y.Z\source\CodexAccountSwitcher-source-vX.Y.Z.zip
release\vX.Y.Z\SHA256SUMS.txt
release\vX.Y.Z\RELEASE_NOTES.md
```

## GitHub upload checklist

For every GitHub release, attach all files from `release\vX.Y.Z\assets`, the source zip from `release\vX.Y.Z\source`, and `SHA256SUMS.txt`.

Keep file names stable so update instructions and README links remain predictable.
