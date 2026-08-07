# Codex Remote API

Codex Account Switcher can run a small local HTTP API for remote control scenarios.
The API is optional. The normal desktop app still starts without it.

Start API mode with:

```powershell
CodexAccountSwitcher.exe --remote-api
```

## Safety Boundaries

- Do not expose the API directly to the public internet.
- Bind to localhost by default.
- If LAN access is required, restrict Windows Firewall to one trusted gateway address.
- Keep `CODEX_REMOTE_API_TOKEN` outside source control.
- Do not commit `.env`, Telegram bot configuration, `auth.json`, profile snapshots, SQLite databases, logs, or generated build output.
- API responses never include raw `auth.json`, bearer tokens, refresh tokens, account IDs, or full usage endpoint responses.
- The Telegram bot is optional. A sanitized, environment-driven template is available in `integrations/telegram-bot/`.
- The desktop **Remote control** page can store and test an external HTTPS connection with a DPAPI-protected token. It does not expose this listener or install a tunnel. See `docs/TELEGRAM_GUI_SETUP.md`.

## Environment Variables

| Variable | Required | Default | Purpose |
| --- | --- | --- | --- |
| `CODEX_REMOTE_API_TOKEN` | yes | none | Bearer token required for every route except `/health`. |
| `CODEX_REMOTE_API_URL` | no | `http://127.0.0.1:8765/` | HttpListener prefix. Use a LAN IP only with firewall restrictions. |
| `CODEX_REMOTE_CODEX_HOME` | no | selected app database value, auto-detected `.codex`, or `%USERPROFILE%\.codex` | Codex Home used by API mode. |
| `CODEX_REMOTE_ALLOWED_REMOTE_ADDRESS` | only for firewall configuration | none | Trusted gateway IP allowed by `/network/configure-firewall` and by the installer script. |
| `V2RAYTUN_TASK_NAME` | no | `CodexStartV2RayTun` | Scheduled task used by V2RayTun start/restart endpoints. |
| `V2RAYTUN_PREFS_PATH` | no | `%APPDATA%\v2RayTun.net\v2RayTun\shared_preferences.json` | V2RayTun preferences file. |
| `V2RAYTUN_CONNECTION_PATH` | no | `%TEMP%\v2RayTun\connection.json` | V2RayTun connection status file. |

## Authentication

All routes except `/health` require:

```http
Authorization: Bearer <CODEX_REMOTE_API_TOKEN>
```

The token is compared in constant time. Token values are not logged or returned.

## Routes

### Public health check

- `GET /health`

Returns only the public service name and health state. It does not disclose the selected Codex Home path.

### Codex status and accounts

- `GET /status`
- `GET /accounts`
- `GET /limits`
- `POST /switch-account`
- `POST /start-codex`
- `POST /stop-codex`

`POST /switch-account` body:

```json
{ "account": "profile-name" }
```

These routes reuse the same local services as the desktop app. Account switching still creates backups and still switches only `auth.json`.

### V2RayTun helpers

- `GET /v2ray/status`
- `POST /v2ray/start`
- `POST /v2ray/proxy`
- `POST /v2ray/restart`

These routes read safe status fields, start the configured scheduled task, and can enforce Proxy Mode in V2RayTun preferences.

### Windows power and network helpers

- `POST /shutdown`
- `POST /reboot`
- `POST /sleep`
- `GET /power/status`
- `POST /power/configure`
- `POST /network/configure-firewall`
- `GET /autologon/status`
- `POST /autologon/ensure`

These routes are intentionally powerful. Use them only behind a trusted local gateway and firewall rule.

`POST /network/configure-firewall` requires `CODEX_REMOTE_ALLOWED_REMOTE_ADDRESS`. It configures the `Codex Remote API from home gateway` firewall rule for the port from `CODEX_REMOTE_API_URL`.

## Scheduled Task Setup

From an elevated PowerShell session:

```powershell
$env:CODEX_REMOTE_API_TOKEN = '<generate-a-long-random-token>'

.\scripts\windows\install-remote-api-task.ps1 `
  -AppExePath 'C:\Path\To\CodexAccountSwitcher.exe' `
  -ApiPrefix 'http://127.0.0.1:8765/' `
  -CodexHome "$env:USERPROFILE\.codex"
```

For LAN gateway access, pass explicit network values:

```powershell
.\scripts\windows\install-remote-api-task.ps1 `
  -AppExePath 'C:\Path\To\CodexAccountSwitcher.exe' `
  -ApiPrefix 'http://<windows-lan-ip>:8765/' `
  -CodexHome "$env:USERPROFILE\.codex" `
  -AllowedRemoteAddress '<trusted-gateway-ip>'
```

To preserve an existing bot/gateway setup after updating the program, rerun the task installer with the same API prefix, Codex Home, allowed gateway address, and token that the bot already uses. New deployments can use the public template in `integrations/telegram-bot/`; copy `.env.example` to a local untracked `.env` and provide deployment-specific values there.

## Local Test

```powershell
$headers = @{ Authorization = "Bearer $env:CODEX_REMOTE_API_TOKEN" }
Invoke-RestMethod http://127.0.0.1:8765/health
Invoke-RestMethod http://127.0.0.1:8765/status -Headers $headers
Invoke-RestMethod http://127.0.0.1:8765/accounts -Headers $headers
```

Do not paste token values, `auth.json`, or raw API responses containing private environment details into public issues.
