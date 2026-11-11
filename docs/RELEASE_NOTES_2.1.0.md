# Codex Account Switcher 2.1.0

Switch profiles as usual. The switcher automatically restores local projects, chat assignments, manual ordering, pins and custom sections, even when the current sidebar is empty. Saving a fresh sign-in or restoring a sign-in backup also runs recovery. Cloud items and drafts stay with their account.

Codex Desktop stores each account’s sidebar separately. Before startup, the switcher merges the outgoing layout and fills missing entries from saved local layouts and the shared Codex database. It uses recorded IDs and section memberships. Chats with no known assignment stay ungrouped. The database is read-only during recovery. If the layout changes, the switcher saves the original using Windows DPAPI and restores it if launch fails.

Windows packages cover x64 and x86. The installers are unsigned. Verify downloads against `SHA256SUMS.txt`.

Every profile change reads the project catalog again after Codex closes. A project committed to SQLite, including its WAL, is matched to sidebar references on the first switch even if the renderer has not saved its project record yet.
