# Telegram integration template

This optional bot controls Codex Account Switcher Remote API through buttons. It contains no deployment-specific host, IP, MAC, Telegram account ID, token, SSH key, or server name.

## Security model

- The bot refuses to start unless `TELEGRAM_BOT_TOKEN`, `TELEGRAM_ALLOWED_USER_IDS`, `REMOTE_API_BASE_URL`, and `REMOTE_API_TOKEN` are explicitly configured.
- Non-loopback Remote API URLs must use HTTPS. Redirects are disabled so bearer tokens are not forwarded to another host.
- Sleep, reboot, and shutdown require a one-time confirmation with a short TTL.
- Account buttons are generated from the live `/accounts` response and switch by the internal profile identifier; display text uses the current profile display name.
- Limit output includes compact 5-hour and weekly percentages and reset times. Backend credential errors are reduced to `ошибка` and do not expose token details.
- Wake-on-LAN needs a separate always-on LAN gateway. Configure its HTTPS endpoint separately; a sleeping or powered-off Windows PC cannot receive an HTTP request itself.
- Keep the bot and Remote API behind a private tunnel or strict firewall allowlist. Do not publish the Windows HTTP listener directly to the Internet.

## Run

1. Copy `.env.example` to a server-only environment file and fill every required value.
2. Install Python 3.12+ dependencies from `requirements.txt`.
3. Load the environment file through your service manager and run `python app.py`.
4. Run `pytest -q` before deployment.

The template intentionally does not include systemd units because service users, paths, tunnel endpoints, and hardening policies are deployment-specific.

For the recommended VPS + home LAN gateway + reverse SSH deployment, use [korsun009/codex-telegram-remote](https://github.com/korsun009/codex-telegram-remote). The desktop GUI workflow and architecture are documented in [`docs/TELEGRAM_GUI_SETUP.md`](../../docs/TELEGRAM_GUI_SETUP.md).
