# Localized Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Russian, English, and Chinese language selection to the app and produce a Windows installer package.

**Architecture:** Keep the existing WinForms/AntdUI app intact and add a small code-based localization layer for visible UI text. Persist language in the existing SQLite Settings table and package the self-contained win-x64 publish output with WiX.

**Tech Stack:** .NET 8 WinForms, AntdUI, SQLite, WiX Toolset SDK.

---

### Task 1: Add App Localization

**Files:**
- Create: `codex-account-switcher/Core/Localization.cs`
- Modify: `codex-account-switcher/MainForm.cs`
- Modify: `codex-account-switcher/Program.cs`
- Test: `codex-account-switcher/CodexAccountSwitcher.Tests/LocalizationTests.cs`

- [ ] Add `AppLanguage` and `Localizer` with `ru`, `en`, and `zh`.
- [ ] Persist selected language as `language` in the existing SQLite Settings table.
- [ ] Use localized text for main window title, menu, settings, sidebar, account cards, dialogs, instructions, and limits screen.
- [ ] Add language buttons to Settings: Русский, English, 中文.
- [ ] Run tests that verify fallback language behavior and key translations.

### Task 2: Build MSI Installer

**Files:**
- Create: `codex-account-switcher/Installer/CodexAccountSwitcher.Installer.wixproj`
- Create: `codex-account-switcher/Installer/Package.wxs`
- Modify: `codex-account-switcher/CodexAccountSwitcher.sln`
- Modify: `codex-account-switcher/README.md`

- [ ] Package the self-contained `win-x64` publish folder into Program Files.
- [ ] Add Start Menu shortcut and uninstall metadata.
- [ ] Keep app runtime data in `%APPDATA%`; do not package any local auth files or database.
- [ ] Document `dotnet publish` and `dotnet build Installer`.

### Task 3: Verify

- [ ] Run `dotnet build`.
- [ ] Run `dotnet test`.
- [ ] Run `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true`.
- [ ] Run `dotnet build codex-account-switcher/Installer/CodexAccountSwitcher.Installer.wixproj -c Release`.
