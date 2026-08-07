# Windows Packaging Policy

- The V2 desktop client is packaged with electron-builder as combined and architecture-specific NSIS installers, x64/ia32 MSI installers, and x64/ia32 portable ZIP files.
- The renderer runs with `contextIsolation`, sandboxing, a fixed preload API and no Node integration.
- The self-contained .NET sidecar is stored under `resources/backend` and receives commands only over stdio.
- The installer does not delete `%APPDATA%\CodexAccountSwitcher`, `%APPDATA%\codex-account-switcher-desktop` or any Codex Home.
- The 2.0.0 release is unsigned. Do not describe it as signed until a trusted Authenticode certificate is configured and verified.
- Unsigned publication requires an explicit release warning and matching SHA-256 checksums; the normal production script continues to reject `NotSigned` output unless `-AllowUnsigned` is provided deliberately.
