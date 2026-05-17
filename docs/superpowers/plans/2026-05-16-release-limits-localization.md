# Codex Account Switcher Release, Limits, and Localization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prepare Codex Account Switcher for public GitHub use with safe Codex usage-limit visibility, a Windows installer, and Russian/English/Chinese presentation.

**Architecture:** Keep the account switcher local-first and conservative: the app may open official OpenAI usage surfaces, but it must not read or replay `auth.json` tokens against undocumented endpoints. Packaging should produce a normal Windows installer from the .NET publish output, while GitHub remains the source and release distribution channel.

**Tech Stack:** .NET 8 WinForms, AntdUI, SQLite, Inno Setup for the first installer, GitHub Actions for repeatable builds, Markdown README files for RU/EN/ZH-CN, and official OpenAI Codex help/developer docs for limit behavior.

---

## Design Decisions

- Limit visibility starts as an in-app read-only usage request based on the same pattern used by public Codex quota utilities: read `tokens.access_token` and `tokens.account_id` from the active `auth.json`, call the ChatGPT usage endpoint once, and discard the token immediately.
- The in-app widget must not refresh tokens, store tokens, print tokens, or write raw usage responses. If the endpoint rejects the current token, the app should show a friendly message and let Codex refresh its own login state.
- The first installer should use Inno Setup because it is simple, free, GitHub Actions friendly, and works well for a small WinForms utility. MSIX can be added later if Start menu integration, signing, and update flow need the Microsoft packaging model.
- Public release must never include runtime files: `auth.json`, `_account_profiles`, `_account_switcher_backups`, SQLite databases, logs, `bin`, `obj`, or local publish artifacts.
- Localization should be resource-backed before the first public release: app strings move into `Resources/Strings.ru.resx`, `Strings.en.resx`, and `Strings.zh-CN.resx`; GitHub gets `README.md`, `README.ru.md`, and `README.zh-CN.md`.

## Official Source Notes

- OpenAI Help says Codex usage limits depend on the ChatGPT plan and task complexity, and users should check the Codex usage page or limit banner when nearing limits.
- OpenAI Developers pricing says local messages and cloud tasks share a five-hour window, additional weekly limits may apply, and current limits can be seen in the Codex usage dashboard; active Codex CLI sessions can use `/status`.
- OpenAI Help also notes Codex usage is available in the Compliance API for supported Codex clients. That is useful for enterprise/workspace analytics, but it is not a replacement for a personal desktop app reading the signed-in user's remaining local limits.

## Task 1: Safe Limit Visibility

**Files:**
- Modify: `codex-account-switcher/MainForm.cs`
- Modify: `codex-account-switcher/README.md`

- [ ] Keep the `Лимиты Codex` menu item and sidebar button.
- [ ] Show a themed in-app screen with 5-hour and weekly limit cards.
- [ ] Read only `access_token` and `account_id` from the active `auth.json`.
- [ ] Request `https://chatgpt.com/backend-api/wham/usage` with `Authorization: Bearer ...` and `ChatGPT-Account-Id`.
- [ ] Parse `primary_window` / `secondary_window`, `five_hour` / `weekly`, and percent field variants such as `percent_left`, `remaining_percent`, and `used_percent`.
- [ ] Mention the CLI `/status` route in documentation as a manual official fallback.
- [ ] Do not print, log, store, or display token values or raw auth JSON.
- [ ] Verify with `dotnet build .\CodexAccountSwitcher.sln`.

## Task 2: Installer

**Files:**
- Create: `installer/CodexAccountSwitcher.iss`
- Create: `.github/workflows/build-release.yml`
- Modify: `README.md`

- [ ] Publish the app as self-contained win-x64:

```powershell
dotnet publish .\codex-account-switcher\codex-account-switcher.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
```

- [ ] Add an Inno Setup script that installs `CodexAccountSwitcher.exe` to `{autopf}\Codex Account Switcher`, creates a Start menu shortcut, and does not install any user `.codex` data.
- [ ] Add a GitHub Actions workflow that restores, tests, publishes, runs Inno Setup, and uploads both the installer and a portable zip as release artifacts.
- [ ] Document that the installer is unsigned until a code-signing certificate is obtained.

## Task 3: Public GitHub Page

**Files:**
- Modify: `README.md`
- Create: `README.ru.md`
- Create: `README.zh-CN.md`
- Create: `SECURITY.md`
- Create: `LICENSE`

- [ ] Make the default `README.md` English for GitHub discoverability.
- [ ] Add language links at the top: English, Русский, 中文.
- [ ] Put a clear download block near the top that points to GitHub Releases.
- [ ] Add screenshots after they are captured from the final UI.
- [ ] Explain the safety model: only `auth.json` snapshots are switched; token contents are never stored in SQLite or printed.
- [ ] Add `SECURITY.md` with instructions not to paste `auth.json` into issues and to report credential-handling bugs privately.
- [ ] Choose and add a permissive license before publishing; MIT is the simplest default for this utility.

## Task 4: App Localization

**Files:**
- Create: `codex-account-switcher/Resources/Strings.ru.resx`
- Create: `codex-account-switcher/Resources/Strings.en.resx`
- Create: `codex-account-switcher/Resources/Strings.zh-CN.resx`
- Modify: `codex-account-switcher/MainForm.cs`

- [ ] Move visible app strings from `MainForm.cs` into resource files.
- [ ] Add an app language setting with options `Автоматически`, `Русский`, `English`, `中文`.
- [ ] Default automatic language to `CultureInfo.CurrentUICulture`.
- [ ] Keep profile names and filesystem paths user-provided; do not translate them.
- [ ] Verify Russian, English, and Chinese layouts do not overflow in the settings panel and account cards.

## Task 5: Release Verification

**Files:**
- Modify: `docs/superpowers/plans/2026-05-16-release-limits-localization.md`

- [ ] Run `dotnet test .\codex-account-switcher\CodexAccountSwitcher.sln`.
- [ ] Run the publish command from Task 2.
- [ ] Start the published app on a clean Windows user profile or VM.
- [ ] Confirm first-run `.codex` detection and manual folder selection.
- [ ] Confirm the GitHub release archive excludes `auth.json`, `_account_profiles`, backups, SQLite databases, logs, `bin`, and `obj`.
- [ ] Update this plan with the exact release artifact names before tagging the first public version.
