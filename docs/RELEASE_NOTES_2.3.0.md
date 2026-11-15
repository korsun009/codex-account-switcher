# Codex Account Switcher 2.3.0

## Перенос профилей между компьютерами

- В настройках добавлены кнопки «Экспортировать профили» и «Импортировать профили» в одной строке.
- Для экспорта выбери аккаунты с сохранённым входом, затем задай и повтори пароль. Результат — файл `.codexprofiles`.
- При импорте выбери файл и введи пароль. Профили добавляются к существующим; совпадающие имена получают уникальное название.
- Файл защищён AES-256-GCM с ключом PBKDF2-HMAC-SHA256. Названия и данные входа зашифрованы вместе; импорт проверяет целостность до добавления профилей.
- Импортированные входы защищаются DPAPI текущего пользователя Windows. Текущий аккаунт Codex не переключается.

Архив переносит сохранённые аккаунты, а не историю чатов, папки проектов, настройки удалённого доступа или резервные копии. Если срок действия входа истёк, Codex может потребовать повторную авторизацию. Забытый пароль восстановить нельзя.

## Downloads

Windows x64 and x86: portable ZIP, MSI and NSIS installers, plus the combined installer and updater metadata. This release is unsigned, as previous releases were; SHA-256 checksums are included.

## Rollback

Keep the previous installer/portable release. Import is additive and does not replace existing profiles or live Codex auth. Remove only newly imported profiles if they are no longer needed.
