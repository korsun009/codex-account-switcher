# Public repository and v1.0.1 artifact audit

Audit date: 2026-08-05

Scope:

- repository `https://github.com/korsun009/codex-account-switcher.git` after fetching every public ref;
- full reachable Git history;
- public v1.0.1 source ZIP, x64/x86 portable ZIPs, and x64/x86 MSI installers;
- unpacked payloads from every v1.0.1 artifact.

Method:

- filename and history inventory for `auth.json`, `.env`, private keys, databases, profile directories and backup directories;
- Gitleaks 8.30.1 full-history scan with redaction;
- Gitleaks directory scan of each separately unpacked release artifact;
- SHA-256 comparison between local artifacts and the public `SHA256SUMS.txt` release asset.

Result: no credential, OAuth token, bearer token, private key, `auth.json`, runtime database, profile snapshot or backup directory was found in Git history or release payloads.

Verified public v1.0.1 hashes:

```text
4B274720865B670779E952F0102BDBC84F4BBF36AB263BA29DBC44DE9C445660  CodexAccountSwitcher-portable-win-x64.zip
C5273F029AC2F8D0A9D1EA77C27C553E2FDE12D66E0CEDFF9488BE8D48B2AEF3  CodexAccountSwitcher-portable-win-x86.zip
A8EEA73D5954469D7F134B99E8BBC113466335606FD0AB439075E32722A44C1F  CodexAccountSwitcherSetup-win-x64.msi
F571E136F267B6B042364739B4212CE656D1F2151811C88249F45283CAC8BE19  CodexAccountSwitcherSetup-win-x86.msi
C690D9AC90BE714D82031C0D415FBA93ED86E12D8A79D8749F9440886E1ADA2C  CodexAccountSwitcher-source-v1.0.1.zip
```

## Personal-data finding

Three personal profile labels were public defaults in `AccountSwitcherService.DefaultProfiles`. A clean installation created those labels even though no matching `auth.json` existed. This explains the screenshot from the second PC. It is an unintended disclosure of labels, not evidence that account tokens were copied through GitHub or a Microsoft account.

V2 removes all default profiles. A clean database starts with zero accounts, and the release audit rejects personal sample names and credential-bearing runtime files before publication.

## Repeatable command

```powershell
pwsh -File scripts/security/audit-release.ps1 -RepositoryRoot . -FixtureMode
```

For a release candidate, pass every unpacked artifact directory through `-ArtifactPaths`. The script downloads only the pinned official Gitleaks 8.30.1 Windows archive and verifies SHA-256 before execution.
