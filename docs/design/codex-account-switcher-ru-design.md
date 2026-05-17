# Codex Account Switcher — Russian UI Design

## Figma Screen Direction

Target frame: desktop utility, 980 x 660.

Mood: calm operational tool, not a landing page. The app should feel safe, quiet, and clear because it touches authentication files.

Palette, revised after the second redesign:

- Background: `#F6F7F9`
- Cards: `#FFFFFF`
- Primary text: `#18202F`
- Muted text: `#697384`
- Primary action/accent: restrained green `#678460`
- Secondary action/accent: restrained orange `#C47E4B`
- Border: `#DCE2EA`
- Dark page: `#151920`
- Dark surface: `#1E242F`
- Dark accent: `#879F78`
- Log background: `#181F2C`

Researched implementation options:

- AntdUI: modern Ant Design-inspired WinForms UI package for .NET 8; good candidate if the project later wants a component dependency.
- KimTools: interesting WinForms design/code tooling with an MCP direction, but too broad for this tiny local auth utility.
- ReaLTaiizor, MaterialSkin, Krypton Toolkit, MetroFramework: useful options, but either heavier, more opinionated, older-looking, or less aligned with the quiet minimal direction.

Decision updated: the app uses native `Form` for the Windows close/minimize/maximize controls and AntdUI for the actual interface controls: `Panel`, `Button`, `Tag`, `Alert`, `Label`, and `Input`.

Layout:

- Header: short product title, current active account, shared `.codex` path.
- Primary area: responsive profile cards. The starter cards are `korsuntop`, `korsunfin009`, `tylerl`, and the settings panel can add more.
- Each account card contains one status sentence, one primary transition action, and one secondary capture action.
- Service area: bottom-left compact utility card with rare actions: clean login, backup, rollback, inventory, file-auth config.
- Log area: bottom-right dark log panel. It is visible but not the visual center.

Implementation note: the current WinForms version implements this direction with AntdUI components, a native menu, a right-side settings panel, adaptive card wrapping, automatic light/dark theme detection from Windows, and a restrained beige/green/orange palette. The native title bar and WinForms scroll containers are also themed through Windows APIs where available.
