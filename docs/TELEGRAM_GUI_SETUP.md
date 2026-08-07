# Настройка Telegram через интерфейс Codex Account Switcher

## Итоговая схема

```text
Telegram -> бот на VPS -> локальный порт reverse SSH на VPS
         -> домашний Linux gateway -> Windows Remote API
         -> Codex Account Switcher / Codex / V2RayTun / питание ПК
```

Windows Remote API не должен быть доступен из интернета. Домашний gateway находится в одной LAN с ПК, отправляет Wake-on-LAN и обращается к API по локальному адресу. Reverse SSH-туннель создаётся исходящим соединением с домашнего сервера на VPS.

## Что настраивается в графическом интерфейсе

1. Откройте **Codex Account Switcher → Настройки** и проверьте правильность **Codex Home**.
2. Откройте **Удалённое управление**. Карточка **Windows API** показывает, видит ли приложение переменную `CODEX_REMOTE_API_TOKEN` в текущем процессе.
3. В блоке **Подключения к сервисам** добавьте диагностическое подключение:
   - название: например `Telegram gateway`;
   - тип: `Telegram`;
   - URL проверки: HTTPS health endpoint gateway или `http://127.0.0.1:<port>/health` только для loopback;
   - секретный токен gateway.
4. Нажмите **Проверить**. Токен шифруется Windows DPAPI и не возвращается обратно в интерфейс.

Эта GUI-запись предназначена для хранения и проверки внешнего подключения. Она не публикует Windows API, не создаёт SSH-туннель и не устанавливает Telegram-бот автоматически.

## Однократная системная настройка Windows API

Remote API запускается с повышенными правами через Планировщик заданий. Из установленной или распакованной папки проекта выполните один раз в PowerShell от администратора:

```powershell
$env:CODEX_REMOTE_API_TOKEN = '<длинный случайный токен>'
pwsh -File .\scripts\windows\install-remote-api-task.ps1 `
  -AppExePath 'C:\Program Files\Codex Account Switcher\Codex Account Switcher.exe' `
  -ApiPrefix 'http://<локальный-IP-Windows>:8765/' `
  -CodexHome "$env:USERPROFILE\.codex" `
  -AllowedRemoteAddress '<локальный-IP-домашнего-gateway>'
```

Используйте зарезервированный DHCP-адрес ПК. Токен должен совпадать с `WINDOWS_API_TOKEN` домашнего gateway. Не вставляйте токен в issue, Telegram-сообщение или git.

## Развёртывание бота и gateway

Используйте отдельный репозиторий [codex-telegram-remote](https://github.com/korsun009/codex-telegram-remote). В нём находятся:

- `bot/app.py` для VPS;
- `home-gateway/app.py` для домашнего Linux-сервера;
- примеры переменных без секретов;
- systemd units для бота, gateway и reverse SSH-туннеля.

Бот разрешает доступ только перечисленным Telegram user IDs. Аккаунты и лимиты не прописываются в боте: они запрашиваются у Codex Account Switcher при каждом открытии списка. Поэтому добавление и удаление профиля в программе автоматически отражается в Telegram.

## Проверка

1. В GUI внешний gateway проходит кнопку **Проверить**.
2. На Windows `GET /health` отвечает локально, а защищённый `GET /status` принимает bearer token.
3. На домашнем сервере gateway видит Windows IP и API, Wake-on-LAN отправляется через LAN.
4. На VPS health endpoint доступен только через локальный конец reverse tunnel.
5. В Telegram работают кнопки статуса, аккаунтов, лимитов, Codex и VPN.
6. Сон, перезагрузка и выключение выполняются только после одноразового подтверждения.
7. После Wake-on-LAN ПК просыпается, автологон завершается, запланированные задачи запускают Remote API и V2RayTun.

Полный контракт маршрутов: [REMOTE_API.md](REMOTE_API.md).
