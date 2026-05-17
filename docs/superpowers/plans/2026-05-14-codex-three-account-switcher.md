# Codex Three Account Switcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a safe Windows `.exe` that switches Codex between three OpenAI accounts while keeping `C:\Users\korsu\.codex` as the single shared Codex home for projects, chats, plugins, skills, MCP configuration, tools, memories, and app settings.

**Architecture:** Keep the standard Codex home path unchanged. The switcher stores only account-specific credential snapshots under `C:\Users\korsu\.codex\_account_profiles`, swaps those files into the live `C:\Users\korsu\.codex` root while Codex is fully closed, and leaves shared files and directories in place. The first implementation treats `auth.json` as the only confirmed account-specific file and adds an auditable discovery mode before adding any third-party OAuth credential files.

**Tech Stack:** .NET 8 Windows desktop executable, WinForms UI after WPF MarkupCompile was proven broken on this machine, PowerShell helper commands for validation and recovery, Figma for UI layout, Canva for icon/visual asset exploration, and official OpenAI Codex documentation as the source for Codex home, config, sessions, MCP, plugin, and auth behavior.

---

This ExecPlan is a living document. The sections `Progress`, `Surprises & Discoveries`, `Decision Log`, and `Outcomes & Retrospective` must be kept up to date as work proceeds.

The repository has `C:\Users\korsu\.codex\PLANS.md`; this document follows that file. A future worker must update this plan before and after each implementation milestone so it remains sufficient to continue from the plan alone.

## Purpose / Big Picture

The user wants Codex account switching to feel like the built-in account switcher: the same projects, the same local chats, the same MCP servers, the same plugins, and the same tools should remain visible, while only the active OpenAI account and account-linked external service authorizations change. The user specifically prefers not changing `CODEX_HOME`, because changing the home path would make the Codex environment feel like a different installation.

After this plan is implemented, the user can open a small Windows executable, choose `Account 1`, `Account 2`, or `Account 3`, and let the app close Codex, switch the credential snapshot, verify the live files, and reopen Codex. The user can prove it works by switching accounts and seeing the Codex UI show the selected account while the project list, local sessions, plugins, MCP servers, and `config.toml` settings remain unchanged.

## Progress

- [x] (2026-05-14 Europe/Moscow) Read `superpowers:writing-plans` guidance and local `C:\Users\korsu\.codex\PLANS.md`.
- [x] (2026-05-14 Europe/Moscow) Attempted `curl.md` for official OpenAI/Codex docs; the local CLI returned `RATE_LIMIT_EXCEEDED`, so official docs were read through OpenAI Developers and the official `openai/codex` repository instead.
- [x] (2026-05-14 Europe/Moscow) Inspected `C:\Users\korsu\.codex` without printing secret values. Confirmed `config.toml`, `auth.json`, `sessions`, `archived_sessions`, `session_index.jsonl`, `state_5.sqlite`, `plugins`, `skills`, `cache`, `memories`, `rules`, and MCP-related config are present.
- [x] (2026-05-14 Europe/Moscow) Chose the one-home architecture: keep `C:\Users\korsu\.codex` live and shared; swap only account-specific files while Codex is closed.
- [x] (2026-05-14 Europe/Moscow) Built a read-only inventory implementation in the desktop app core that classifies shared state versus candidate account credential files and writes a non-secret JSON report.
- [x] (2026-05-14 Europe/Moscow) Built account-file backup and rollback implementation for `auth.json`; full `.codex` recursive backup is intentionally deferred because the safe first implementation backs up only account-specific files.
- [x] (2026-05-14 Europe/Moscow) Built an `auth.json`-only switch proof of concept inside the desktop app core and UI. It refuses missing profile auth, closes Codex before switching, saves the previous live auth back to the active profile, writes a pre-switch backup, replaces live `auth.json`, writes an active marker, and relaunches Codex.
- [ ] Manually enroll the second and third accounts, producing separate `auth.json` snapshots without copying refresh tokens between profiles.
- [ ] Verify whether third-party OAuth credentials live in `.codex` files, OS credential storage, or remote connector state, then extend the account-specific file list only if evidence requires it.
- [x] (2026-05-14 Europe/Moscow) Created a Figma mockup page named `Codex Account Switcher` in the configured Figma test file and generated Canva logo/icon direction candidates.
- [x] (2026-05-14 Europe/Moscow) Implemented a WinForms `.exe` with safe switching, inventory, capture, file-auth config helper, backup, rollback, and clear status/log output.
- [x] (2026-05-14 Europe/Moscow) Moved the working project and chat transcript backup into `X:\Documents\Проекты Codex\Перемещение между аккаунтами Codex` so future Codex sessions can continue from a normal project folder.
- [x] (2026-05-14 Europe/Moscow) Re-added the test project to `CodexAccountSwitcher.sln`, added a safety regression test for corrupt `active-profile.json`, and republished the executable.
- [x] (2026-05-14 Europe/Moscow) Created live runtime directories `C:\Users\korsu\.codex\_account_profiles\acc1..acc3` and `C:\Users\korsu\.codex\_account_switcher_backups`, then created and hash-verified a pre-enrollment backup of the current live `auth.json`.
- [x] (2026-05-14 Europe/Moscow) Renamed the visible account labels to `korsuntop`, `korsunfin009`, and `tylerl` while keeping internal profile directories as `acc1`, `acc2`, and `acc3`.
- [x] (2026-05-14 Europe/Moscow) Translated the WinForms app to Russian, improved the visual layout with rounded cards and action colors, and added local Figma/Canva design briefs under `docs/design` because plugin write tools were not exposed in this session. `dotnet test CodexAccountSwitcher.sln` now passes 9/9 tests.
- [x] (2026-05-14 Europe/Moscow) Investigated revoked refresh tokens after enrolling multiple accounts. Root cause is likely normal Codex logout revoking the saved refresh token. Added a `Чистый вход` flow that backs up and removes live `auth.json` without invoking logout, then relaunches Codex for signing into the next account. `dotnet test` now covers this flow.
- [x] (2026-05-14 Europe/Moscow) Researched modern WinForms UI options, including AntdUI, KimTools, ReaLTaiizor, MaterialSkin, Krypton Toolkit, and MetroFramework. Added AntdUI 2.3.12 and rebuilt the UI around AntdUI controls (`Window`, `Panel`, `Button`, `Tag`, `Alert`, `Input`) for a stronger visual redesign.
- [ ] Run end-to-end tests with all three real accounts and document the final file classification.

## Surprises & Discoveries

- Observation: `curl.md` is installed at `C:\Users\korsu\AppData\Roaming\npm\curl.md.cmd`, but fetching docs currently fails due to a rate limit.
  Evidence: the command returned `RATE_LIMIT_EXCEEDED` and suggested `curl.md auth login`.

- Observation: The current `config.toml` already contains project trust records, enabled plugins, and MCP server definitions.
  Evidence: a redacted key-only scan showed `[projects.'...']`, `[plugins."figma@openai-curated"]`, `[plugins."canva@openai-curated"]`, `[plugins."superpowers@openai-curated"]`, and `[mcp_servers.*]` sections.

- Observation: The Codex app is currently running multiple `Codex.exe` processes plus a `codex.exe` helper.
  Evidence: `Get-Process` showed several `Codex` processes under `C:\Program Files\WindowsApps\OpenAI.Codex_26.506.3741.0_x64__2p2nqsd0c76g0\app\...`. The switcher must close all related processes before swapping credentials.

- Observation: Official OpenAI docs state that Codex stores local state under `CODEX_HOME`, defaulting to `~/.codex`, and common files include `config.toml`, `auth.json` when file-based credentials are used, history, logs, and caches. The app troubleshooting docs identify session transcripts at `$CODEX_HOME/sessions` and archived sessions at `$CODEX_HOME/archived_sessions`.
  Evidence: OpenAI Developers pages for Codex advanced config and app troubleshooting.

- Observation: A clean WPF project created with the installed .NET 8 SDK crashes during `MarkupCompilePass1` with `System.AccessViolationException`. A clean console app and a clean WinForms app build successfully.
  Evidence: `dotnet build` on WPF smoke projects for `net8.0` and `net7.0` reproduced the same `AccessViolationException`; `dotnet build` on a WinForms smoke project succeeded.

- Observation: The machine has .NET SDK 8.0.407, Windows Desktop runtimes 7.0.2, 8.0.14, and 10.0.5, but no .NET 10 SDK.
  Evidence: `dotnet --info` listed SDK 8.0.407 only. The implementation therefore targets `net8.0-windows`.

- Observation: Figma write access worked through the configured Figma file.
  Evidence: `use_figma` created page `Codex Account Switcher` and frame `Desktop app mockup - safe account switcher` in file key `a4jITToIPzNOHj2S1eQX4v`.

- Observation: Canva generated four icon direction candidates for the desktop utility.
  Evidence: Canva generation job `f05e8956-e185-4c07-bb13-c04d9bc3c06b` returned four candidate IDs beginning with `dg-0c48...`, `dg-281...`, `dg-843...`, and `dg-d76...`.

## Decision Log

- Decision: Do not switch `CODEX_HOME`.
  Rationale: The user wants all projects, chats, tools, plugins, skills, MCP servers, and settings to remain identical across accounts. Keeping `C:\Users\korsu\.codex` as the only live home best matches that requirement.
  Date/Author: 2026-05-14 / Codex

- Decision: Start with `auth.json` as the only confirmed account-specific file.
  Rationale: OpenAI docs explicitly identify `auth.json` as the file-based credential cache. Other files, such as `state_5.sqlite`, `.codex-global-state.json`, browser profiles, and plugin caches may contain a mix of shared UI state and account-linked data, so they must not be moved without proof.
  Date/Author: 2026-05-14 / Codex

- Decision: Require `cli_auth_credentials_store = "file"` in the shared `config.toml` before switching.
  Rationale: The switcher can only safely manage credentials that are file-backed. If credentials are stored in Windows Credential Manager, file swapping `auth.json` may not control the active account.
  Date/Author: 2026-05-14 / Codex

- Decision: Use snapshots, not symlinks, for account credentials.
  Rationale: Refresh tokens can change when Codex refreshes a session. The live `auth.json` should be copied back into the active profile after Codex closes, so the selected profile retains the newest token state.
  Date/Author: 2026-05-14 / Codex

- Decision: The switcher must include backup and rollback as first-class UI actions.
  Rationale: This touches auth material under the user's primary Codex home. Recovery must be possible without knowing internal implementation details.
  Date/Author: 2026-05-14 / Codex

- Decision: Use WinForms instead of WPF for the first executable.
  Rationale: The local WPF compiler crashes even for a fresh template, while WinForms builds successfully and still produces a native Windows `.exe`. This keeps the project moving without installing or repairing SDK components midstream.
  Date/Author: 2026-05-14 / Codex

- Decision: Use .NET 8 rather than .NET 10 for the build.
  Rationale: Only .NET SDK 8.0.407 is installed. A self-contained `net8.0-windows` executable satisfies the Windows `.exe` requirement without adding machine-level SDK changes.
  Date/Author: 2026-05-14 / Codex

## Outcomes & Retrospective

Initial implementation is complete through the safe `auth.json`-only proof of concept. The app has not yet switched the live `C:\Users\korsu\.codex` to another account because doing so may close the current Codex session and change active auth. Unit tests pass against temporary test homes, a self-contained publish artifact exists at `X:\Documents\Проекты Codex\Перемещение между аккаунтами Codex\codex-account-switcher\bin\Release\net8.0-windows\win-x64\publish\CodexAccountSwitcher.exe`, live account profile directories now exist, and a verified pre-enrollment `auth.json` backup exists. Design work has a Figma mockup plus Canva icon candidates.

## Context and Orientation

`C:\Users\korsu\.codex` is the user's standard Codex home. A "Codex home" means the local directory where Codex stores user-level configuration and local state. In this user's installation it contains:

`config.toml`, the shared Codex configuration. It includes model settings, sandbox settings, project trust records, plugin enablement, and MCP server definitions. This file must remain shared.

`auth.json`, the file-backed OpenAI authentication cache when Codex is configured to store credentials in files. This is the first and only confirmed account-specific file.

`sessions` and `archived_sessions`, local conversation transcript directories. These must remain shared because the user wants the same chats across accounts.

`session_index.jsonl`, `state_5.sqlite`, and `logs_2.sqlite`, app state and index/log files. These are shared by default. They must not be added to account profiles unless a later test proves that a specific field inside them is account-only and cannot remain shared.

`plugins`, `skills`, `cache`, `rules`, `memories`, and MCP definitions in `config.toml`, which represent the user's tool environment. These must remain shared.

The switcher project lives outside the live Codex home while being developed, under `X:\Documents\Проекты Codex\Перемещение между аккаунтами Codex\codex-account-switcher`. The runtime account snapshots should live inside the Codex home at `C:\Users\korsu\.codex\_account_profiles` because the user asked for the standard `.codex` path to remain the center of the setup. Backup archives should live at `C:\Users\korsu\.codex\_account_switcher_backups`.

The official docs that matter are embedded here in summary form. Codex uses `CODEX_HOME` for local state and defaults it to `~/.codex`. Codex user configuration is stored in `~/.codex/config.toml`; project config can also exist under project `.codex/config.toml`. Codex app agents inherit the same configuration as CLI and IDE, and MCP configuration lives in `config.toml`. The app troubleshooting docs identify local sessions at `$CODEX_HOME/sessions` and archived sessions at `$CODEX_HOME/archived_sessions`. The config reference says `cli_auth_credentials_store` controls whether cached credentials are stored in file-backed `auth.json`, an OS keychain, or automatic selection. The official app-server docs expose thread APIs, plugin APIs, MCP OAuth login APIs, and config write APIs; this confirms that threads, plugins, MCP, and config are app-server concepts separate from the raw OpenAI auth file.

## Plan of Work

Milestone 1 is a read-only inventory and safety baseline. Create scripts that list files, sizes, timestamps, and SHA-256 hashes without reading or printing secret contents. Classify known shared files and candidate credential files. The acceptance criterion is a report that says `auth.json` is account-specific, `config.toml` is shared, sessions are shared, and all unknown files remain shared until proven otherwise.

Milestone 2 is backup and rollback. Build a script that stops Codex, copies the live `C:\Users\korsu\.codex` to a timestamped backup directory using safe PowerShell copy operations, records a manifest with hashes, and can restore the backup when Codex is closed. The acceptance criterion is that running backup twice creates two separate backups and restore can replace `auth.json` from the backup without touching sessions unless explicitly requested.

Milestone 3 is a command-line proof of concept for `auth.json` switching. The script must close all Codex processes, save the current live `auth.json` into the currently active profile, replace live `auth.json` from the target profile, verify the hash changed to the target hash, and reopen Codex through its AUMID or executable path. The acceptance criterion is that Account 1 and Account 2 can be switched while `config.toml`, `sessions`, `session_index.jsonl`, and `state_5.sqlite` hashes remain unchanged.

Milestone 4 is account enrollment. For each of three accounts, the user signs in manually once while that account is active. The switcher then captures the resulting live `auth.json` into `C:\Users\korsu\.codex\_account_profiles\<profile>\auth.json`. The acceptance criterion is that each profile has a distinct `auth.json` hash and Codex opens as the expected account after switching.

Milestone 5 is third-party auth discovery. Use only evidence-based expansion: switch accounts, connect one external service in one account, close Codex, compare file hashes and timestamps before and after, and identify whether any local `.codex` file changes besides `auth.json`. If the evidence shows OAuth tokens are remote or stored by the app outside `.codex`, keep the switcher at `auth.json` only. If the evidence shows a local credential file such as `.credentials.json`, add that file to the account-specific manifest with explicit tests. The acceptance criterion is a written classification file that says exactly which files are switched and why.

Milestone 6 is UI design. Create a Figma design with a compact desktop utility layout: three account buttons, current active account, Codex process status, last backup status, last switch result, backup button, rollback button, and a small log panel. Use Canva for the app icon and optional compact brand asset. The UI should be quiet, operational, and not a landing page. The acceptance criterion is a Figma frame and Canva icon asset that can be implemented directly in WinForms or a future WPF version after the local WPF toolchain is repaired.

Milestone 7 is the Windows `.exe`. Implement the app in .NET 8 WinForms for the current machine. The app must have a safe core service with no UI dependencies, a desktop shell, and tests for file operations. The core service must never delete recursively outside `C:\Users\korsu\.codex\_account_profiles` or `C:\Users\korsu\.codex\_account_switcher_backups`. It must use atomic replacement where possible, keep a per-switch emergency backup of the previous live `auth.json`, and write logs that never contain credential contents.

Milestone 8 is verification. Run unit tests, publish a single-file Windows `.exe`, run through all three account switches, verify the shared state hashes remain stable, and verify rollback. The acceptance criterion is a release directory containing the `.exe`, a short user guide, and a final manifest of switched files.

## Concrete Steps

Create the project directory:

    cd /d X:\Documents\Проекты Codex\Перемещение между аккаунтами Codex
    dotnet new winforms -n codex-account-switcher -f net8.0
    cd codex-account-switcher
    dotnet new xunit -n CodexAccountSwitcher.Tests
    dotnet new sln -n CodexAccountSwitcher
    dotnet sln add codex-account-switcher.csproj
    dotnet sln add CodexAccountSwitcher.Tests\CodexAccountSwitcher.Tests.csproj

The original plan preferred WPF, but WPF MarkupCompile crashes on this machine. If WPF is repaired later, run:

    dotnet new list wpf

and repair the .NET Desktop workload before migrating the shell. Do not fall back to a web app because the deliverable must be a Windows `.exe`.

Create the runtime directories:

    New-Item -ItemType Directory -Force C:\Users\korsu\.codex\_account_profiles
    New-Item -ItemType Directory -Force C:\Users\korsu\.codex\_account_switcher_backups

Create three profile directories:

    New-Item -ItemType Directory -Force C:\Users\korsu\.codex\_account_profiles\acc1
    New-Item -ItemType Directory -Force C:\Users\korsu\.codex\_account_profiles\acc2
    New-Item -ItemType Directory -Force C:\Users\korsu\.codex\_account_profiles\acc3

Before editing `config.toml`, read it and verify whether `cli_auth_credentials_store` exists. If missing, add this top-level line:

    cli_auth_credentials_store = "file"

Do not change model, sandbox, projects, plugin, MCP, app, or tool sections. After editing, verify that the key-only structure still includes the existing `[projects.*]`, `[plugins.*]`, and `[mcp_servers.*]` sections.

The initial switched-file manifest must be:

    C:\Users\korsu\.codex\auth.json

The initial shared-file denylist must include:

    C:\Users\korsu\.codex\config.toml
    C:\Users\korsu\.codex\sessions
    C:\Users\korsu\.codex\archived_sessions
    C:\Users\korsu\.codex\session_index.jsonl
    C:\Users\korsu\.codex\state_5.sqlite
    C:\Users\korsu\.codex\state_5.sqlite-shm
    C:\Users\korsu\.codex\state_5.sqlite-wal
    C:\Users\korsu\.codex\plugins
    C:\Users\korsu\.codex\skills
    C:\Users\korsu\.codex\cache
    C:\Users\korsu\.codex\memories
    C:\Users\korsu\.codex\rules
    C:\Users\korsu\.codex\tools

Implement a core method with this behavior:

    SwitchTo(profileName):
      verify profileName is one of acc1, acc2, acc3
      verify C:\Users\korsu\.codex exists
      stop Codex.exe and codex.exe processes
      wait until no Codex.exe or codex.exe processes remain, or fail with a clear message
      if live auth.json exists, copy it to the previously active profile
      copy live auth.json to _account_switcher_backups\pre-switch-<timestamp>\auth.json
      replace live auth.json with _account_profiles\<profileName>\auth.json
      write active profile marker to _account_profiles\active-profile.json
      launch Codex again
      return status without printing token contents

The active-profile marker should contain only non-secret metadata:

    {
      "activeProfile": "acc1",
      "lastSwitchUtc": "2026-05-14T00:00:00Z"
    }

The app must refuse to switch if the target profile has no `auth.json`, and it must show the user the manual login flow for enrolling that profile.

## Validation and Acceptance

Run the tests:

    cd /d X:\Documents\Проекты Codex\Перемещение между аккаунтами Codex\codex-account-switcher
    dotnet test

Expected result: all tests pass. The tests must cover path containment, profile validation, manifest parsing, backup creation, rollback selection, process-close timeout behavior with mocked process services, and secret-redacted logging.

Run the publish command:

    dotnet publish .\codex-account-switcher.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true

Expected result: `CodexAccountSwitcher.exe` appears under `bin\Release\net8.0-windows\win-x64\publish`.

Manual acceptance:

Open Codex as Account 1. Record hashes for shared files using the inventory script. Switch to Account 2 through the app. Confirm Codex opens and shows Account 2. Re-run the inventory script. `config.toml`, `sessions`, `archived_sessions`, `session_index.jsonl`, `state_5.sqlite`, plugin folders, skill folders, and MCP config must be unchanged or only show expected normal runtime timestamp changes. `auth.json` must match the selected account profile. Repeat for Account 3.

Rollback acceptance:

Use the app's rollback button to restore the previous live `auth.json` backup. Open Codex and confirm the account reverts. Shared chats and projects must still be visible.

Third-party OAuth acceptance:

For each connector the user cares about, switch to a profile and open the connector's status in Codex. If the connector remains tied to the intended account, record that no local file expansion was required. If it does not, run the file-diff discovery step and add the exact credential file to the profile manifest only after proving it is not a shared chat/project/plugin state file.

## Idempotence and Recovery

All operations must be safe to repeat. Creating profile directories with `-Force` is safe. Running the backup script repeatedly creates new timestamped backups. Switching to the currently active profile should still refresh that profile's stored `auth.json` and reopen Codex, but it should warn the user that no account change was needed.

If a switch fails after Codex closes but before relaunch, the app must offer to restore the newest pre-switch backup. If the app cannot determine the active profile, it must not guess; it should preserve the live `auth.json`, ask the user to label it as one of the three profiles, and then continue.

Never print `auth.json`, OAuth token values, bearer tokens, or environment variable values. Logs may include file names, hashes, byte counts, timestamps, profile names, and process IDs.

Before any recursive remove or move, resolve the absolute target path and prove it starts with either `C:\Users\korsu\.codex\_account_profiles\` or `C:\Users\korsu\.codex\_account_switcher_backups\`. The app should not recursively delete anything else.

## Artifacts and Notes

Official source summary used for this plan:

OpenAI Developers Codex configuration basics says user-level config lives at `~/.codex/config.toml`, project-level config can live in project `.codex/config.toml`, and Codex configuration can set model, approvals, sandbox, and MCP servers.

OpenAI Developers Codex advanced config says local state lives under `CODEX_HOME`, defaulting to `~/.codex`, and common files include `config.toml`, `auth.json` when file-based credential storage is used, history, logs, and caches.

OpenAI Developers Codex app settings says agents in the app inherit the same configuration as the IDE and CLI, and MCP configuration lives in `config.toml`.

OpenAI Developers Codex app troubleshooting says session transcripts live at `$CODEX_HOME/sessions` and archived sessions live at `$CODEX_HOME/archived_sessions`.

OpenAI Developers Codex config reference says `cli_auth_credentials_store` controls cached credential storage, including file-backed `auth.json` versus OS keychain.

The official `openai/codex` app-server README says app-server initialization returns `codexHome`, exposes thread APIs for session history, exposes plugin and app APIs, exposes MCP OAuth login APIs, and exposes config read/write APIs. This supports treating auth, config, threads, plugins, and MCP as separate surfaces.

Current local inventory summary:

    C:\Users\korsu\.codex\auth.json exists and is 4657 bytes.
    C:\Users\korsu\.codex\config.toml exists and `cli_auth_credentials_store` is not currently set.
    C:\Users\korsu\.codex\_account_profiles\acc1, acc2, and acc3 exist.
    C:\Users\korsu\.codex\_account_switcher_backups contains a verified pre-enrollment backup of auth.json.
    C:\Users\korsu\.codex\sessions and C:\Users\korsu\.codex\archived_sessions exist.
    C:\Users\korsu\.codex\plugins, skills, cache, memories, rules, tools, sqlite, and browser-related directories exist.
    Running Codex processes were observed under OpenAI.Codex_26.506.3741.0.

## Interfaces and Dependencies

Create a core library namespace inside the WinForms project or a separate class library if tests become easier that way. The core must expose these plain interfaces so file and process behavior can be tested without touching the real `.codex` folder:

    public interface IFileSystem
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        byte[] ReadAllBytes(string path);
        void WriteAllBytesAtomic(string path, byte[] bytes);
        void CopyFile(string sourcePath, string destinationPath, bool overwrite);
        string ComputeSha256(string path);
        IReadOnlyList<FileInventoryItem> EnumerateInventory(string rootPath);
    }

    public interface ICodexProcessService
    {
        IReadOnlyList<CodexProcessInfo> FindRunningCodexProcesses();
        Task StopCodexAsync(TimeSpan timeout, CancellationToken cancellationToken);
        Task LaunchCodexAsync(CancellationToken cancellationToken);
    }

    public sealed record AccountProfile(string Name, string DisplayName, string DirectoryPath, bool HasAuthJson);

    public sealed record SwitchResult(bool Success, string ActiveProfile, string Message, string BackupDirectory);

    public sealed class AccountSwitcherService
    {
        public Task<SwitchResult> SwitchToAsync(string profileName, CancellationToken cancellationToken);
        public Task<string> CreateBackupAsync(CancellationToken cancellationToken);
        public Task<SwitchResult> RestoreBackupAsync(string backupDirectory, CancellationToken cancellationToken);
    }

The WinForms UI must call only `AccountSwitcherService`; it must not perform raw file copies itself. This keeps safety rules centralized.

The final executable should be named `CodexAccountSwitcher.exe`. The final user-facing app title should be `Codex Account Switcher`.

## Revision Notes

2026-05-14 / Codex: Initial ExecPlan created from the user's requirement to keep `C:\Users\korsu\.codex` shared while switching among three OpenAI accounts through a future Windows executable. The plan records the conservative `auth.json`-first strategy and requires evidence before switching any additional credential files.

2026-05-14 / Codex: Updated after implementation of the first desktop executable. WPF was replaced with WinForms because WPF MarkupCompile crashes reproducibly on this machine. The safe core, WinForms shell, tests, README, and published `CodexAccountSwitcher.exe` now exist. Real-account enrollment and third-party OAuth discovery remain.

2026-05-14 / Codex: Added design evidence. Figma now has the desktop utility mockup in the configured test file, and Canva produced four logo/icon candidates. The executable itself still uses the WinForms shell pending real-account testing and optional icon selection.

2026-05-14 / Codex: Continued from the chat transcript after moving the project to `X:\Documents\Проекты Codex\Перемещение между аккаунтами Codex`. The solution now includes the test project again, a corrupt active-profile marker no longer allows switching after closing Codex, and the republished executable includes that safety fix. Live `.codex` inspection found `auth.json` and `config.toml` present, but no account profile or switcher backup directories yet.
