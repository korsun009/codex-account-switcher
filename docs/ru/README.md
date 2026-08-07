# Codex Account Switcher для Windows

[Главная](../../README.md) | [English](../en/README.md) | [中文](../zh/README.md)

Codex Account Switcher — открытая Windows-программа для безопасного переключения нескольких аккаунтов OpenAI Codex, просмотра пятичасовых и недельных лимитов и удалённого управления через Telegram. Чаты, проекты, MCP-серверы, плагины, skills и настройки остаются общими; меняется только активный вход.

## Скачать 2.0.0

Файлы находятся в [GitHub Releases](https://github.com/korsun009/codex-account-switcher/releases/latest). Проверяйте их по `SHA256SUMS.txt`.

| Windows | Установщик с автообновлением | Установщик архитектуры | MSI | Portable |
| --- | --- | --- | --- | --- |
| 10/11 x64 | `Codex-Account-Switcher-2.0.0-Setup.exe` | `Codex-Account-Switcher-2.0.0-x64-Setup.exe` | `Codex-Account-Switcher-2.0.0-x64-Setup.msi` | `Codex-Account-Switcher-2.0.0-x64-Portable.zip` |
| 10/11 x86 | `Codex-Account-Switcher-2.0.0-Setup.exe` | `Codex-Account-Switcher-2.0.0-ia32-Setup.exe` | `Codex-Account-Switcher-2.0.0-ia32-Setup.msi` | `Codex-Account-Switcher-2.0.0-ia32-Portable.zip` |

`ia32` означает 32-битную x86-сборку. .NET отдельно устанавливать не нужно. macOS, Linux, Windows 8 и 8.1 не поддерживаются.

> Версия 2.0.0 пока не подписана Authenticode-сертификатом. Windows может показать «Неизвестный издатель» или SmartScreen. Контрольные суммы проверяют целостность файла, но не заменяют подпись доверенного издателя.

## Возможности

- Добавление, удаление и переключение профилей Codex через графический интерфейс.
- Шифрование сохранённых входов Windows DPAPI для текущего пользователя Windows.
- Проверка `auth.json`, зашифрованные резервные копии и откат при ошибке.
- Пятичасовые и недельные лимиты по всем профилям без показа токенов.
- Запуск и остановка Codex, стабильный канал обновлений и четыре темы.
- Русский, английский и китайский интерфейс.
- Опциональный Remote API и Telegram-бот с русскими кнопками.

## Быстрый старт

1. Установите рекомендованный `.exe`, MSI или распакуйте portable ZIP.
2. Откройте программу и проверьте найденную папку Codex Home.
3. Нажмите **Добавить аккаунт**, задайте понятное имя и откройте Codex из мастера.
4. Войдите в нужный аккаунт вручную, вернитесь в программу и сохраните вход.
5. Переключайтесь кнопкой **Перейти**. Не используйте обычный logout в Codex между сохранёнными профилями: он может отозвать refresh token.

NSIS `.exe` рекомендуется для автоматического обновления. Пользователи MSI и portable могут проверить новую версию в приложении, а затем обновиться вручную из GitHub Releases.

## Безопасность и переносимость

Программа переключает только активный `auth.json`. Снимки профилей и резервные копии хранятся в `auth.dpapi`, привязанном к текущему пользователю Windows. Старый открытый `auth.json` профиля удаляется только после успешного шифрования, расшифровки, проверки структуры и сравнения байтов.

DPAPI-файлы нельзя использовать для переноса входов на другой ПК или к другому Windows-пользователю. При синхронизации папки могут появиться названия профилей, но зашифрованные входы на другом ПК не расшифруются. На новом ПК каждый вход нужно сохранить заново.

## Telegram

Шаблон прямого подключения находится в [`integrations/telegram-bot`](../../integrations/telegram-bot). Рекомендуемая схема VPS → защищённый обратный SSH-туннель → домашний gateway → Windows API находится в отдельном репозитории [codex-telegram-remote](https://github.com/korsun009/codex-telegram-remote).

Кнопки управляют статусом и Wake-on-LAN, аккаунтами, лимитами, Codex, V2RayTun Proxy Mode, сном, перезагрузкой и выключением. Опасные действия требуют одноразового подтверждения. Аккаунты всегда читаются из программы, поэтому список обновляется автоматически.

Пошаговая настройка: [графический интерфейс и Telegram](../TELEGRAM_GUI_SETUP.md). API нельзя открывать напрямую в интернет.

## Переводы

Я принимаю переводы от сообщества. Сделайте fork, измените нужный язык в [`translations.json`](../../desktop/src/renderer/src/locales/translations.json), сохраните ключи и `{{placeholders}}`, выполните тесты и откройте pull request. Подробности: [как добавить перевод](../TRANSLATIONS.md).

## Сборка

```powershell
dotnet test .\codex-account-switcher\CodexAccountSwitcher.sln --configuration Release
Push-Location .\desktop
pnpm install --frozen-lockfile
pnpm typecheck
pnpm test
Pop-Location
pwsh -File .\scripts\build-release.ps1
```

См. [изменения 2.0.0](../RELEASE_NOTES_2.0.0.md), [участие в разработке](../../CONTRIBUTING.md) и [модель безопасности](../../SECURITY.md).
