# Password-protected profile transfer — 2.3.0

Goal: Export selected saved Codex logins into one portable .codexprofiles file and import them on another Windows account without altering live auth or shared chats.

Architecture: C# backend owns credential read, authenticated archive encryption, validation and atomic import. Electron main owns OS file pickers and enforces payload limits/trusted sender. Renderer shows a small selectable profile dialog, then password/confirmation; import asks for file/password. No credentials in renderer or plaintext temporary files. Import creates new safe internal IDs and unique display names on collisions, preserves all existing profiles, validates the entire file before mutation and rolls back newly added profiles on failure. Windows DPAPI protects imported credentials at rest.

Format: versioned envelope; AES-256-GCM with random 16-byte salt, 12-byte nonce and 16-byte tag. PBKDF2-HMAC-SHA256 fixed 600000 iterations, cryptographic RNG; header authenticated. Password minimum 12 characters and maximum 1024; archive maximum 16 MiB, maximum 1000 profiles, validate all auth documents and names. Reject unsupported format/version and malformed lengths before KDF. Clear plaintext/key buffers in finally. No password logs or argv. Export never sends plaintext credentials to renderer.

Implementation steps:
- [ ] Baseline dotnet test and frontend test/typecheck.
- [ ] Add ProfileTransferService + tests: selection, cross-protector roundtrip, wrong password, tamper/header/version/oversize, duplicate names, rollback and no live auth writes; run red then green.
- [ ] Add bounded backend commands exportProfiles/importProfiles, registered through existing protocol, with explicit lock/conflict handling.
- [ ] Add dedicated Electron save/open flows that accept no renderer-supplied path; validate sender/payload. Transfer ciphertext only across bridge if needed. No general filesystem exposure.
- [ ] Add settings row and accessible export/import dialogs in all supported locales; selection then password; confirmation, busy/cancel/error/success, no empty selection or profiles lacking saved credentials.
- [ ] Add renderer/IPC tests and packaged E2E through Settings -> export -> select -> password -> file, then import -> wrong password -> correct password, check no partial import and existing profiles retained. Use synthetic profiles only.
- [ ] Independent specification and security/code review; fix findings.
- [ ] Bump all release version references to 2.3.0, document format and release notes.
- [ ] Full backend/frontend tests, package x64/x86 portable/MSI/NSIS, verify package metadata and isolated functional behavior; security audit and checksums.
- [ ] User explicitly authorized merge and GitHub release: merge once into main, check gh auth korsun009 before each GitHub write, push main/tag, upload verified release assets and updater metadata, verify latest/assets. Do not install into user's active profile.

Execution uses subagent-driven-development for implementation and independent requesting-code-review before merge. Existing user request authorizes implementation, merge and publication without additional approval gates. No unrelated Codex unread patch or compatibility changes.
