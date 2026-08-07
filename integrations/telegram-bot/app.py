from __future__ import annotations

import json
from typing import Any

import httpx
from telegram import InlineKeyboardButton, InlineKeyboardMarkup, Update
from telegram.ext import Application, CallbackQueryHandler, CommandHandler, ContextTypes

from bot_core import ACTION_ROUTES, DESTRUCTIVE_ACTIONS, ConfirmationStore, Settings, format_response


SETTINGS = Settings.from_environment()
CONFIRMATIONS = ConfirmationStore(SETTINGS.confirm_ttl_seconds)
COMMAND_ACTIONS = {
    "status": "status",
    "accounts": "accounts",
    "limits": "limits",
    "codex_start": "codex_start",
    "codex_stop": "codex_stop",
    "vpn_status": "vpn_status",
    "vpn_start": "vpn_start",
    "vpn_proxy": "vpn_proxy",
    "vpn_restart": "vpn_restart",
    "wake": "wake",
}


def keyboard() -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup([
        [InlineKeyboardButton("Статус ПК", callback_data="do:status"), InlineKeyboardButton("Аккаунты", callback_data="do:accounts")],
        [InlineKeyboardButton("Лимиты Codex", callback_data="do:limits"), InlineKeyboardButton("Запустить Codex", callback_data="do:codex_start")],
        [InlineKeyboardButton("Остановить Codex", callback_data="do:codex_stop"), InlineKeyboardButton("Включить ПК", callback_data="do:wake")],
        [InlineKeyboardButton("VPN статус", callback_data="do:vpn_status"), InlineKeyboardButton("Запустить VPN", callback_data="do:vpn_start")],
        [InlineKeyboardButton("Proxy Mode", callback_data="do:vpn_proxy"), InlineKeyboardButton("Перезапустить VPN", callback_data="do:vpn_restart")],
        [InlineKeyboardButton("Сон", callback_data="do:sleep"), InlineKeyboardButton("Перезагрузка", callback_data="do:reboot")],
        [InlineKeyboardButton("Выключить ПК", callback_data="do:shutdown")],
    ])


def accounts_keyboard(data: Any) -> InlineKeyboardMarkup:
    rows = []
    if isinstance(data, list):
        for item in data:
            if not isinstance(item, dict):
                continue
            profile_name = item.get("name")
            display_name = item.get("displayName") or profile_name
            if not isinstance(profile_name, str) or not isinstance(display_name, str):
                continue
            label = ("✓ " if item.get("active") else "") + display_name
            rows.append([InlineKeyboardButton(label[:48], callback_data=f"switch:{profile_name}")])
    rows.append([InlineKeyboardButton("Назад", callback_data="do:status")])
    return InlineKeyboardMarkup(rows)


def authorized(update: Update) -> bool:
    return bool(update.effective_user and update.effective_user.id in SETTINGS.allowed_user_ids)


async def start(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if not authorized(update) or not update.effective_message:
        return
    await update.effective_message.reply_text("Управление компьютером", reply_markup=keyboard())


async def callback(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    query = update.callback_query
    if not query or not authorized(update):
        return
    await query.answer()
    _, action = (query.data or "do:status").split(":", maxsplit=1)

    if action in DESTRUCTIVE_ACTIONS:
        CONFIRMATIONS.issue(update.effective_user.id, action)
        markup = InlineKeyboardMarkup([[
            InlineKeyboardButton("Подтвердить", callback_data=f"confirm:{action}"),
            InlineKeyboardButton("Отмена", callback_data="cancel:action"),
        ]])
        await query.edit_message_text("Подтвердите действие в течение короткого времени.", reply_markup=markup)
        return

    if action == "wake":
        text = await call_wake()
    elif action == "accounts":
        payload, error = await request_remote(*ACTION_ROUTES["accounts"])
        if error or payload is None:
            await query.edit_message_text(error or "Сервис временно недоступен.", reply_markup=keyboard())
            return
        await query.edit_message_text(
            format_response("accounts", payload),
            reply_markup=accounts_keyboard(payload.get("data")),
        )
        return
    else:
        text = await call_remote(action)
    await query.edit_message_text(text, reply_markup=keyboard())


async def switch_account(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    query = update.callback_query
    if not query or not authorized(update):
        return
    await query.answer()
    _, profile_name = (query.data or "switch:").split(":", maxsplit=1)
    payload, error = await request_remote("POST", "/switch-account", {"account": profile_name})
    text = error or format_response("switch_account", payload)
    await query.edit_message_text(text, reply_markup=keyboard())


async def confirm(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    query = update.callback_query
    if not query or not authorized(update):
        return
    await query.answer()
    prefix, action = (query.data or "cancel:action").split(":", maxsplit=1)
    if prefix == "cancel":
        await query.edit_message_text("Действие отменено.", reply_markup=keyboard())
        return
    if not CONFIRMATIONS.consume(update.effective_user.id, action):
        await query.edit_message_text("Подтверждение истекло.", reply_markup=keyboard())
        return
    await query.edit_message_text(await call_remote(action), reply_markup=keyboard())


async def call_remote(action: str) -> str:
    route = ACTION_ROUTES.get(action)
    if not route:
        return "Неизвестное действие."
    method, path = route
    payload, error = await request_remote(method, path)
    return error or format_response(action, payload)


async def request_remote(method: str, path: str, body: dict[str, Any] | None = None) -> tuple[dict[str, Any] | None, str | None]:
    headers = {"Authorization": f"Bearer {SETTINGS.remote_api_token}"}
    try:
        async with httpx.AsyncClient(timeout=SETTINGS.request_timeout_seconds, follow_redirects=False) as client:
            response = await client.request(method, SETTINGS.remote_api_base_url + path, headers=headers, json=body)
        if response.status_code >= 400:
            return None, f"Сервис вернул ошибку HTTP {response.status_code}."
        payload = response.json()
        if not isinstance(payload, dict):
            return None, "Сервис вернул некорректный ответ."
        return payload, None
    except (httpx.HTTPError, ValueError, json.JSONDecodeError):
        return None, "Сервис временно недоступен."


async def call_wake() -> str:
    if not SETTINGS.wake_endpoint_url or not SETTINGS.wake_endpoint_token:
        return "Wake-on-LAN gateway не настроен."
    try:
        async with httpx.AsyncClient(timeout=SETTINGS.request_timeout_seconds, follow_redirects=False) as client:
            response = await client.post(
                SETTINGS.wake_endpoint_url,
                headers={"Authorization": f"Bearer {SETTINGS.wake_endpoint_token}"},
            )
        return "Команда включения отправлена." if response.is_success else f"Gateway вернул HTTP {response.status_code}."
    except httpx.HTTPError:
        return "Wake-on-LAN gateway недоступен."


async def command_action(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    if not authorized(update) or not update.effective_message:
        return
    command = update.effective_message.text.split()[0].lstrip("/").split("@", maxsplit=1)[0]
    action = COMMAND_ACTIONS.get(command, "status")
    text = await call_wake() if action == "wake" else await call_remote(action)
    await update.effective_message.reply_text(text, reply_markup=keyboard())


def main() -> None:
    application = Application.builder().token(SETTINGS.bot_token).build()
    application.add_handler(CommandHandler("start", start))
    for command in COMMAND_ACTIONS:
        application.add_handler(CommandHandler(command, command_action, has_args=False))
    application.add_handler(CallbackQueryHandler(confirm, pattern=r"^(confirm|cancel):"))
    application.add_handler(CallbackQueryHandler(switch_account, pattern=r"^switch:"))
    application.add_handler(CallbackQueryHandler(callback, pattern=r"^do:"))
    application.run_polling(drop_pending_updates=True)


if __name__ == "__main__":
    main()
