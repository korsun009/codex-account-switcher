# Codex Account Switcher 2.1.1

Switching profiles now restores local project and chat membership in custom sidebar sections on Codex Desktop builds that store each account’s sidebar separately from the shared project list. Existing sections are matched by their local host section ID. The earlier renderer snapshot format remains supported.

During shutdown, the switcher checks for remaining Codex process trees until the deadline and closes surviving package helpers. A separately launched Node runtime is ignored, while Node processes in the desktop tree are still included. A timeout before the sign-in changes now reports that the original sign-in stayed active.

Windows installers and portable archives are available for x64 and x86. The installers are unsigned. Verify downloads against `SHA256SUMS.txt`.
