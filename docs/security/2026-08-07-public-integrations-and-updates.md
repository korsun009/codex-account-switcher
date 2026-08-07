# Public integrations and updates security review

Date: 2026-08-07

## Boundaries

- Remote connection secrets are encrypted with Windows DPAPI CurrentUser and never returned through bridge list responses.
- Non-loopback connection endpoints require HTTPS. Redirects, embedded credentials, fragments, invalid schemes and overlong values are rejected.
- Telegram deployment data is accepted only through environment variables. The tracked template has no token, owner/chat ID, host, MAC, account list or private server name.
- The public `/health` response exposes no filesystem path.
- Update metadata comes from one fixed GitHub repository and beta channel. No GitHub token is embedded.
- Because the installer is not Authenticode-signed, the app does not auto-download or auto-execute an update; it checks metadata and opens the official release page for a deliberate user download.
- NSIS uses a fixed GUID compatible with the previous beta and keeps `deleteAppDataOnUninstall: false`.

## Audit result

The pinned Gitleaks scan and filename policy passed for reachable Git history, the current source inventory, the final installer and unpacked application. Searches for the known private infrastructure markers and personal default profile names returned no distributable matches. The GitHub repository owner remains in the fixed official repository/update URL by design.
