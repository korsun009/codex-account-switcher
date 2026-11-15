# 2.3.0 verification

Environment: Windows 11 host, .NET 8 backend, Electron 43.3.0. Synthetic accounts and isolated Codex homes/databases only. No live account switch, authentication replacement, or user's Codex restart was performed.

## Completed checks

- Backend: 123 tests passed (0 failed/skipped).
- Frontend/main IPC: 37 tests passed across 10 files; TypeScript and production build passed.
- Independent security/specification review: strict registry read, supported legacy identifiers, Unicode collision handling, IPC sender/path restrictions and rollback reviewed; three initial findings fixed and covered by regression tests.
- Packaged backend x64 -> x86 -> x64 archive transfer passed. Selected profiles only, wrong-password no-op, collision suffix, destination DPAPI-only credential file and unchanged live-auth sentinel verified.
- Synthetic cross-protector test verifies portability independent of the source user's credential protector.
- Packaged UI transfer passed on x64 and x86: Accounts -> Settings -> Export profiles -> selection -> password/confirmation -> file -> Import profiles -> wrong password -> correct password -> Accounts. Existing and imported profiles are both present; live-auth sentinel unchanged. Only OS picker return values are controlled by the test; renderer, IPC, backend, encryption and DPAPI execute unchanged.
- Screenshots of settings, profile selection and resulting accounts inspected: both buttons share one row, compact centered dialog, no clipping. x64 completed without retry; x86 required one visible bootstrap Retry after a cold-start timeout on the busy HDD.
- Source and Git history secret scans passed; portable credential archives are excluded from Git and prohibited as release contents.
- All 11 payload SHA-256 digests generated. Source/stage hard-link identity verified independently. Both MSI ProductVersion/platform values, embedded app.asar MSI file hashes and backend size/version match the packaged applications. Both packaged backend hashes match published backend outputs; portable ZIP app.asar/backend contents match unpacked files. Updater version/path and installer SHA-512 match.

## Build observations

Concurrent MSI packaging exhausted the shared WiX tool lock retry period on the busy HDD. The release build script now creates MSI packages sequentially from the already packaged applications. Both architectures produced MSI packages successfully with this sequence.

Release hashing now uses 8 MiB sequential buffers and caches completed digests while artifacts remain immutable. The helper was checked against the standard SHA-256 implementation and independently reviewed.

Initial UI test runs experienced native startup and backend bootstrap timeouts during disk/memory pressure. A separate immutable test copy, explicit readiness checks and the application's visible Retry action are used to distinguish fixture cold-start delays from feature behavior. Failed attempts are retained in local diagnostic artifacts; they are not counted as passing acceptance tests.

## Final acceptance

Final x64 packaged run: transfer passed plus five general UI tests (primary navigation, layout, sandbox boundary, continuation setting persistence and RU/EN/ZH/theme sizing). The separate chat-snapshot fixture test was explicitly skipped in this combined run; no update-install test was run. Final distribution secret scan passed across all payloads and both unpacked applications. Publication asset-digest verification is recorded in the local release evidence after upload.

## Scope limits

Cross-architecture execution uses the same Windows host. A separate physical computer/Windows user is not used for this release; cross-user protection is covered by independent-protector tests and destination Windows DPAPI writes. MSI install/uninstall and automatic update application are not run against the owner's active installation. Packages retain the existing unsigned-release policy.
