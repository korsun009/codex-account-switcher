# Icon Installer Settings Scroll Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the supplied application icon to portable and installer builds, upgrade the MSI to a normal selectable installer flow, and fix profile-list scrolling in Settings.

**Architecture:** Embed a generated `.ico` in the WinForms executable via MSBuild so portable and installed builds share the same icon. Use WiX UI extension with an install-directory dialog and an optional desktop-shortcut feature. Rework the Settings panel layout so fixed theme/language sections do not starve the profile list.

**Tech Stack:** .NET 8 WinForms, WiX Toolset 6, WiX UI extension.

---

### Task 1: Project Memory and Icon

**Files:**
- Modify: `AGENTS.md`
- Create: `codex-account-switcher/Assets/AppIcon.ico`
- Modify: `codex-account-switcher/codex-account-switcher.csproj`
- Modify: `codex-account-switcher/MainForm.cs`

- [ ] Add a project instruction that release-facing changes must be applied to both portable and installer builds.
- [ ] Convert the supplied PNG icon into a multi-size Windows `.ico`.
- [ ] Set `ApplicationIcon` in the app project.
- [ ] Set the WinForms window icon from the embedded icon.

### Task 2: Normal Installer

**Files:**
- Modify: `codex-account-switcher/Installer/CodexAccountSwitcher.Installer.wixproj`
- Modify: `codex-account-switcher/Installer/Package.wxs`

- [ ] Add `WixToolset.UI.wixext`.
- [ ] Add WixUI install-directory flow so users can choose install location.
- [ ] Add optional desktop-shortcut feature selected by default.
- [ ] Use the same icon for Start Menu shortcut, desktop shortcut, and Programs & Features.

### Task 3: Settings Scrolling

**Files:**
- Modify: `codex-account-switcher/MainForm.cs`

- [ ] Change Settings panel from fully fixed rows to a scrollable content area plus fixed close button.
- [ ] Keep theme and language controls reachable.
- [ ] Give the profile list enough height and resize row widths correctly.

### Task 4: Verify

- [ ] Run `dotnet test`.
- [ ] Run `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true`.
- [ ] Run `dotnet build codex-account-switcher/Installer/CodexAccountSwitcher.Installer.wixproj -c Release`.
