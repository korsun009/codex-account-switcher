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
