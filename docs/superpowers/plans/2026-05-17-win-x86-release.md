# Win X86 Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce separate 32-bit Windows builds for GitHub releases without adding Windows 8/8.1 support.

**Architecture:** Keep the current x64 portable and MSI pipeline unchanged. Add `win-x86` as a supported runtime identifier, publish a separate portable x86 folder, and add a dedicated x86 WiX installer project that packages the x86 publish output into Program Files on 32-bit Windows.

**Tech Stack:** .NET 8 WinForms, WiX Toolset 6, WiX UI extension.

---

### Task 1: Add Win-X86 Runtime

**Files:**
- Modify: `codex-account-switcher/codex-account-switcher.csproj`

- [ ] Add `win-x86` to `RuntimeIdentifiers` next to `win-x64`.
- [ ] Verify both `dotnet publish -r win-x64` and `dotnet publish -r win-x86` produce self-contained executable outputs.

### Task 2: Add X86 MSI

**Files:**
- Create: `codex-account-switcher/InstallerX86/CodexAccountSwitcher.InstallerX86.wixproj`
- Create: `codex-account-switcher/InstallerX86/Package.wxs`
- Modify: `codex-account-switcher/CodexAccountSwitcher.sln`

- [ ] Package `bin/Release/net8.0-windows/win-x86/publish/CodexAccountSwitcher.exe`.
- [ ] Use `InstallerPlatform=x86`.
- [ ] Use `ProgramFilesFolder`, not `ProgramFiles64Folder`.
- [ ] Keep the same install UI behavior: directory selection and optional desktop shortcut.
- [ ] Use the shared app icon.

### Task 3: Document Release Artifacts

**Files:**
- Modify: `README.md`
- Modify: `codex-account-switcher/README.md`

- [ ] Document x64 and x86 portable paths.
- [ ] Document x64 and x86 MSI paths.
- [ ] State that Windows 10/11 are supported, Windows 8/8.1 are not targeted.

### Task 4: Verify

- [ ] Run `dotnet test`.
- [ ] Run x64 publish.
- [ ] Run x86 publish.
- [ ] Build x64 MSI.
- [ ] Build x86 MSI.
