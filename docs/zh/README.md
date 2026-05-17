# Codex Account Switcher

## 这是什么

Codex Account Switcher 是一个本地 Windows 工具，用来在同一台电脑上安全切换多个 Codex/OpenAI 账号。它适合需要多个 Codex 登录状态，但不想反复退出、重新登录、破坏 refresh token 或重新配置本地 Codex 环境的用户。

程序会保持项目、聊天、MCP 服务器、插件、skills 和工具设置共享。真正被切换的只是当前 Codex 登录状态。

适合以下情况：

- 同一台电脑上有工作账号和个人账号；
- 不同项目使用不同 Codex 账号；
- 有一个专门用于测试的账号；
- 多个账号需要共享同一套本地 Codex 配置。

## 解决的问题

Codex 会把当前登录状态保存在 `auth.json` 文件中。普通的 Codex logout 可能会撤销 refresh token。之后保存的登录可能失效，需要重新手动登录。

Codex Account Switcher 不使用普通 logout 流程。它为每个配置文件保存独立的本地登录快照，并且只切换 `auth.json`。`.codex` 中的其他内容保持共享。

这样可以快速切换账号，同时保留同一个本地 Codex 工作环境。

## 下载

请从 [GitHub Releases](https://github.com/korsun009/codex-account-switcher/releases/latest) 下载最新版本。每个版本提供四个主要文件：

| 系统 | 安装包 | 便携版 |
| --- | --- | --- |
| Windows 10/11 x64 | `CodexAccountSwitcherSetup-win-x64.msi` | `CodexAccountSwitcher-portable-win-x64.zip` |
| Windows 10/11 x86 | `CodexAccountSwitcherSetup-win-x86.msi` | `CodexAccountSwitcher-portable-win-x86.zip` |

不支持 Windows 8/8.1。用户不需要单独安装 .NET。

## 程序功能

- 首次启动时自动查找 `.codex` 文件夹。
- 如果自动查找失败，会要求用户手动选择文件夹。
- 可以在程序界面中添加新的 Codex 配置文件。
- 使用向导流程：创建配置文件、打开 Codex、手动登录、保存登录状态。
- 一键切换当前活动配置文件。
- 显示当前正在使用的账号。
- 显示所有已保存配置文件的 Codex 限额。
- 使用本地 SQLite 数据库存储配置文件列表。
- 在替换当前登录状态之前创建备份。
- 支持俄语、英语和中文界面。
- 支持自动、深色、灰色和浅色主题。
- 自动模式会跟随 Windows 应用主题。

## 程序不会做什么

- 不会把 token 或账号数据上传到服务器。
- 不会把 `auth.json` 内容写入 SQLite 数据库。
- 不会按账号隔离项目、聊天、MCP、插件、skills 或工具。
- 不会绕过 OpenAI 或 Codex 的使用限额。
- 不会更改订阅、账号权限或访问规则。
- 不支持 Windows 8/8.1。

## 如何使用

1. 安装 MSI，或解压 portable zip。
2. 启动程序。
3. 首次启动时，程序会自动查找 `.codex`；如果找不到，请手动选择文件夹。
4. 点击 `添加账号`。
5. 输入清晰的配置文件名称。
6. 点击 `打开 Codex`，然后在 Codex 中手动登录需要的账号。
7. 回到程序并点击 `保存登录`。
8. 在配置文件卡片上点击 `切换` 来切换账号。

## 添加新账号

添加账号通过程序内的向导完成。用户仍然控制整个过程，但不需要记住多个工具按钮的顺序。

1. 打开 `添加账号`。
2. 输入配置文件名称，例如 `工作 Codex` 或 `个人账号`。
3. 点击 `创建配置文件`。
4. 点击 `打开 Codex`。
5. 在 Codex 中手动登录需要的账号。
6. 回到 Codex Account Switcher。
7. 点击 `保存登录`。

之后，该配置文件会出现在列表中，可以用于切换。

## 安全模型

- 只切换 `auth.json`。
- 项目、聊天、MCP、插件和工具保持共享。
- `auth.json` 内容不会保存到 SQLite 数据库。
- 替换当前登录前会创建备份。
- 令牌不会显示在界面或日志中。

重要：保存多个账号时，不要使用 Codex 的普通 logout 按钮。普通退出可能会撤销 refresh token，并导致已保存的登录失效。

## Codex 限额

`Codex 限额` 页面会显示所有已保存配置文件的 5 小时和每周限额。程序只从每个本地配置文件的登录快照中读取必要字段，请求 usage endpoint，并且不会把 bearer token 保存到数据库。

这个功能只用于显示信息。它不会增加限额，也不会尝试绕过限额。

## 安装版和便携版

程序有两种使用方式：

- MSI 安装包：像普通 Windows 程序一样安装，可以通过 Windows 设置或 uninstall 文件卸载。
- Portable zip：解压文件夹后直接运行，不需要安装。

两种方式都提供 x64 和 x86 版本。面向用户的更新应当在四个发布版本中全部验证。

## 从源码构建

需要 Windows 10/11、.NET 8 SDK，以及安装包项目所需的 WiX Toolset 支持。

```powershell
dotnet test codex-account-switcher\CodexAccountSwitcher.sln
.\scripts\build-release.ps1 -Version v1.0.1
```

该脚本会生成 portable x64、portable x86、MSI x64、MSI x86、源码压缩包和 `SHA256SUMS.txt`。
