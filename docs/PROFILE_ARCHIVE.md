# Portable profile archive format v1

Extension: `.codexprofiles`. This is a Codex Account Switcher format, not an OpenAI import format. Files can be imported by a compatible Switcher installation on another computer. No account activation occurs during import.

## Binary envelope

All integer fields are little-endian. Maximum complete file size: 16 MiB.

| Offset | Bytes | Value |
| --- | --- | --- |
| 0 | 8 | ASCII `CDXPROFS` |
| 8 | 1 | Version `1` |
| 9 | 16 | Random PBKDF2 salt |
| 25 | 12 | Random GCM nonce |
| 37 | 4 | Ciphertext length |
| 41 | variable | Ciphertext |
| end - 16 | 16 | Authentication tag |

The entire 41-byte header is AES-GCM associated data. AES-256-GCM uses a 32-byte key derived from the exact password (no trimming or normalization), PBKDF2-HMAC-SHA256 with 600,000 iterations. A new cryptographic salt and nonce are generated for every export. Password length: 12–1024 UTF-16 code units, as validated by both application layers. Unsupported versions, invalid lengths and oversized files fail before key derivation.

## Encrypted payload

.NET BinaryWriter representation: Int32 record count (1–1000), followed by each profile's UTF-8 display-name string with a 7-bit encoded byte-length prefix, Int32 auth-document byte length, and original validated auth-document bytes. There are no filesystem paths or source-machine DPAPI blobs. Trailing data is rejected.

All records must validate before registry publication. Destination profile names are generated locally; display-name collisions receive a numeric suffix. The destination wraps credentials with Windows CurrentUser DPAPI. Existing profiles and live auth remain untouched. The archive is an account snapshot, not synchronization; exporting again is necessary after changing saved credentials.

## Security notes

The archive contains credentials even though it is encrypted; do not commit it, attach it to bug reports, or publish it as a release asset. Password loss cannot be recovered. Import cannot renew expired or revoked provider tokens. Byte buffers are cleared after use where possible; .NET/JavaScript immutable strings and operating-system memory are not guaranteed to be erased.

Implementation follows the [Microsoft AesGcm contract](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt?view=net-8.0), including authenticated headers and fresh nonces. The file format must remain versioned for future algorithm changes.
