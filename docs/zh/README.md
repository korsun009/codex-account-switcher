# Codex Account Switcher

## 这是什么

Codex Account Switcher 是一个本地 Windows 工具，用来在同一台电脑上安全切换多个 Codex/OpenAI 账号。

当你有工作账号、个人账号或多个独立 Codex 账号，但希望项目、聊天、MCP、插件、skills 和工具设置保持共享时，它会很有用。

## 解决的问题

普通的 Codex logout 可能会撤销 refresh token。之后保存的登录可能失效，需要重新手动登录。本程序不执行普通 logout。它只切换账号登录文件 `auth.json`，并保持其他 Codex 本地状态共享。

## 下载

GitHub Releases 提供四个主要文件：

| 系统 | 安装包 | 便携版 |
| --- | --- | --- |
| Windows 10/11 x64 | `CodexAccountSwitcherSetup-win-x64.msi` | `CodexAccountSwitcher-portable-win-x64.zip` |
| Windows 10/11 x86 | `CodexAccountSwitcherSetup-win-x86.msi` | `CodexAccountSwitcher-portable-win-x86.zip` |

不支持 Windows 8/8.1。用户不需要单独安装 .NET。

## 如何使用

1. 安装 MSI，或解压 portable zip。
2. 启动程序。
3. 首次启动时，程序会自动查找 `.codex`；如果找不到，请手动选择文件夹。
4. 点击 `添加账号`。
5. 输入清晰的配置文件名称。
6. 点击 `打开 Codex`，然后在 Codex 中手动登录需要的账号。
7. 回到程序并点击 `保存登录`。
8. 在配置文件卡片上点击 `切换` 来切换账号。

## 安全模型

- 只切换 `auth.json`。
- 项目、聊天、MCP、插件和工具保持共享。
- `auth.json` 内容不会保存到 SQLite 数据库。
- 替换当前登录前会创建备份。
- 令牌不会显示在界面或日志中。

## Codex 限额

`Codex 限额` 页面会显示所有已保存配置文件的 5 小时和每周限额。程序只从 `auth.json` 读取必要字段，请求 usage endpoint，并且不会保存令牌。
