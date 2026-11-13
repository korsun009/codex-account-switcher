# Chat state and continuation: 2.2.0

## Storage and sequence

1. Keep the single live Codex home. Never copy sessions or replace its SQLite database.
2. While desktop is running, read local Codex chat history through the desktop App Tools pipe. Page through all history and inspect the last turn with up to six concurrent read-only calls. Preserve unloaded failures too. Snapshot must complete within 90 seconds; a partial snapshot is not committed.
3. Store the snapshot in `_account_profiles/chat-session.dpapi` using current-user Windows DPAPI and atomic replacement. It contains local titles, state, last turn IDs, error text, and continuation outcomes; never auth tokens.
4. Close Codex using existing shutdown logic, back up auth and global state, transfer local sidebar and read markers, replace only authorized account credentials, launch Codex.
5. If enabled, re-read each previously active chat. Send only when the last turn ID matches and its state is interrupted/inProgress, with a known idle/notLoaded status and no waiting flags. Unknown or absent statuses fail closed. The entire continuation phase has a 60-second deadline. Read calls can retry desktop startup; sends never retry.
6. Commit `attempted` before dispatch and `sent` after acknowledgment. A lost response becomes `uncertain`. Reload the same operation journal to prevent repeat dispatch after process restart. This guarantees at most one dispatch per operation, not remote exactly-once execution.

## Compatibility boundaries

Read-state identity is SHA256(JSON(["chatgpt", accountId, userId])); renderer ownership is SHA256(JSON([userId, accountId])). Do not interchange them. Only valid local host arrays are overwritten, including empty arrays. Unknown global versions are left intact. Legacy `unread-thread-ids-by-host-v1` is copied only from the outgoing renderer, never resurrected from historical donors.

The local pipe protocol is version-sensitive: little-endian length-prefixed JSON-RPC, namespace codex_app, tools list_threads/read_thread/send_message_to_thread. Authentication/permission validation remains in the installed desktop. Multiple ambiguous pipes fail safely. The continuation feature requires these tools; project switching remains compatible with older desktops when it is off.

## Acceptance

Automated tests cover old/new unread stores, empty read clears, remote isolation, history pagination, unloaded failures, waiting states, changed/completed/already-running turns, ambiguous-send persistence, default-off and persisted settings, capture failure before shutdown, continuation failure after auth commit, and switching with desktop closed.

Native App Tools was verified read-only against installed Codex 26.1106.6628.0. Real account switching and sending continuation to the owner's active chats are deliberately not triggered by automated validation; those require the owner's normal switch after installing the candidate. Tests cannot establish whether old Codex error badges reappear, and this version does not promise that behavior.

## Verified candidate, 2026-11-13

- Backend: 114 tests passed in Release configuration. The unknown/missing runtime-status regression first failed both cases, then passed with the fail-closed check.
- Frontend: TypeScript check and all 32 unit/component tests passed.
- Packaged Electron: 6 end-to-end tests passed on x64 and 6 on ia32, in isolated homes and application databases. Includes DPAPI snapshot rendering with a long error message, persisted default-off toggle after reload, sandbox isolation, languages, and layout widths.
- All 11 release payloads on the project drive matched the SSD build by SHA-256; the output folder contains those files plus SHA256SUMS.txt. MSI ProductVersion and platform templates were checked for both architectures. Backend and app.asar bytes inside both portable ZIPs match the tested unpacked applications. Updater version, installer path and SHA-512 match the universal installer.
- Gitleaks 8.30.1 and the repository artifact-name policy passed for Git history, current source and packaged artifacts. This is a detection-based scan, not a guarantee about all possible secrets.
- All five EXE/MSI installers are NotSigned, consistent with earlier releases. No installer was applied over the owner's installation; live account switching, continuation of real active tasks, and applying an automatic update remain owner acceptance checks.

Build source remains in the original project. An SSD staging copy was used after explicit owner authorization because the HDD was saturated. Electron 43.3.0 was supplied from an existing local archive after verification against the package's pinned SHA-256 checksums. Final artifacts and verification evidence were copied back under `Материалы релизов` in the main project folder. No live Codex auth or session files were edited by the tests.
