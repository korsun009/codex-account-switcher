# Project Instructions

This project builds a safe local Windows account switcher for Codex.

- Keep `C:\Users\korsu\.codex` as the single shared live Codex home.
- Do not move or duplicate shared chat/project/tool state unless explicitly requested.
- Treat `auth.json` and any OAuth/token/credential file as secret.
- Never print token contents, full bearer values, or copied credential JSON in chat or logs.
- Before changing live Codex auth files, create a backup and verify rollback.
- Prefer evidence-based expansion: switch only `auth.json` until a before/after inventory proves another file is account-specific.
- The current implementation uses WinForms because WPF MarkupCompile crashes on this machine.
- Release-facing changes must be applied and verified for every supported program version: x64 portable, x86 portable, x64 MSI installer, and x86 MSI installer.
