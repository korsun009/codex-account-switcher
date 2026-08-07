# Windows Codex Account Switcher

[Main README](../../README.md) | [Русский](../ru/README.md) | [English](../en/README.md)

Codex Account Switcher 是一款开源 Windows 桌面应用，用于安全切换多个 OpenAI Codex 账号、查看 5 小时和每周限额，并可通过 Telegram 远程控制。聊天、项目、MCP、插件、skills 和设置保持共享，只切换当前登录状态。

## 下载 2.0.0

请从 [GitHub Releases](https://github.com/korsun009/codex-account-switcher/releases/latest) 下载，并使用 `SHA256SUMS.txt` 验证文件。

| Windows | 自动更新安装包 | 架构安装包 | MSI | 便携版 |
| --- | --- | --- | --- | --- |
| 10/11 x64 | `Codex-Account-Switcher-2.0.0-Setup.exe` | `Codex-Account-Switcher-2.0.0-x64-Setup.exe` | `Codex-Account-Switcher-2.0.0-x64-Setup.msi` | `Codex-Account-Switcher-2.0.0-x64-Portable.zip` |
| 10/11 x86 | `Codex-Account-Switcher-2.0.0-Setup.exe` | `Codex-Account-Switcher-2.0.0-ia32-Setup.exe` | `Codex-Account-Switcher-2.0.0-ia32-Setup.msi` | `Codex-Account-Switcher-2.0.0-ia32-Portable.zip` |

`ia32` 表示 32 位 x86 Windows。构建包已包含运行环境。不支持 macOS、Linux、Windows 8 和 8.1。

> v2.0.0 尚未使用 Authenticode 证书签名。Windows 可能显示未知发布者或 SmartScreen 警告。SHA-256 校验值只能验证完整性，不能替代可信发布者签名。

## 主要功能

- 在图形界面中添加、删除和切换 Codex 配置文件。
- 使用 Windows DPAPI CurrentUser 加密保存的登录和备份。
- 在替换 `auth.json` 前进行验证、备份和回滚保护。
- 显示各配置文件的 5 小时和每周 Codex 限额，不显示 token。
- 用户分别确认检查、下载和安装更新。
- 俄语、英语、中文界面和四种主题。
- 可选的认证 Remote API 和 Telegram 按钮控制。

## 使用与安全

安装推荐的 NSIS EXE、MSI，或解压便携版。通过应用向导创建配置文件，打开 Codex 手动登录，然后返回应用保存。不要在已保存配置文件之间使用 Codex 普通 logout，因为它可能撤销 refresh token。

应用只切换当前 `auth.json`。保存的登录以 `auth.dpapi` 加密，并绑定当前 Windows 用户。它不能作为账号凭据复制到另一台电脑或另一个 Windows 用户。同步后可能看到配置文件名称，但必须在新电脑上重新保存登录。

## Telegram 与社区翻译

请查看[图形界面和 Telegram 设置](../TELEGRAM_GUI_SETUP.md)、[Remote API](../REMOTE_API.md)以及独立部署仓库 [codex-telegram-remote](https://github.com/korsun009/codex-telegram-remote)。

欢迎社区翻译。Fork 本仓库，编辑 [`desktop/src/renderer/src/locales/translations.json`](../../desktop/src/renderer/src/locales/translations.json)，保持所有 key 和 `{{placeholders}}` 一致，运行验证，然后提交 pull request。详细说明见 [TRANSLATIONS.md](../TRANSLATIONS.md)。

构建和贡献说明见 [Main README](../../README.md)、[CONTRIBUTING.md](../../CONTRIBUTING.md) 和 [2.0.0 release notes](../RELEASE_NOTES_2.0.0.md)。
