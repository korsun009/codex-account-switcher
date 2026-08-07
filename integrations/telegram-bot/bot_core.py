from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
import os
import time
from typing import Any
from urllib.parse import urlparse
from zoneinfo import ZoneInfo


ACTION_ROUTES: dict[str, tuple[str, str]] = {
    "status": ("GET", "/status"),
    "accounts": ("GET", "/accounts"),
    "limits": ("GET", "/limits"),
    "codex_start": ("POST", "/start-codex"),
    "codex_stop": ("POST", "/stop-codex"),
    "vpn_status": ("GET", "/v2ray/status"),
    "vpn_start": ("POST", "/v2ray/start"),
    "vpn_proxy": ("POST", "/v2ray/proxy"),
    "vpn_restart": ("POST", "/v2ray/restart"),
    "sleep": ("POST", "/sleep"),
    "reboot": ("POST", "/reboot"),
    "shutdown": ("POST", "/shutdown"),
}

DESTRUCTIVE_ACTIONS = frozenset({"sleep", "reboot", "shutdown"})

ACTION_SUCCESS_MESSAGES = {
    "codex_start": "Запуск Codex запрошен.",
    "codex_stop": "Остановка Codex запрошена.",
    "vpn_start": "Запуск V2RayTun запрошен.",
    "vpn_proxy": "Для V2RayTun сохранен режим Proxy Mode.",
    "vpn_restart": "Перезапуск V2RayTun запрошен.",
    "sleep": "Переход компьютера в сон запрошен.",
    "reboot": "Перезагрузка компьютера запрошена.",
    "shutdown": "Выключение компьютера запрошено.",
    "switch_account": "Аккаунт Codex переключен.",
}


@dataclass(frozen=True)
class Settings:
    bot_token: str
    allowed_user_ids: frozenset[int]
    remote_api_base_url: str
    remote_api_token: str
    wake_endpoint_url: str | None
    wake_endpoint_token: str | None
    request_timeout_seconds: float
    confirm_ttl_seconds: int

    @staticmethod
    def from_environment() -> "Settings":
        token = _required("TELEGRAM_BOT_TOKEN")
        remote_token = _required("REMOTE_API_TOKEN")
        allowed_raw = _required("TELEGRAM_ALLOWED_USER_IDS")
        try:
            allowed = frozenset(int(value.strip()) for value in allowed_raw.split(",") if value.strip())
        except ValueError as exc:
            raise RuntimeError("TELEGRAM_ALLOWED_USER_IDS must contain comma-separated integers.") from exc
        if not allowed:
            raise RuntimeError("TELEGRAM_ALLOWED_USER_IDS must not be empty.")

        base_url = _https_url(_required("REMOTE_API_BASE_URL"), allow_loopback_http=True)
        wake_url_raw = os.getenv("WAKE_ENDPOINT_URL", "").strip()
        wake_url = _https_url(wake_url_raw, allow_loopback_http=False) if wake_url_raw else None
        wake_token = os.getenv("WAKE_ENDPOINT_TOKEN", "").strip() or None
        if bool(wake_url) != bool(wake_token):
            raise RuntimeError("WAKE_ENDPOINT_URL and WAKE_ENDPOINT_TOKEN must be configured together.")

        return Settings(
            bot_token=token,
            allowed_user_ids=allowed,
            remote_api_base_url=base_url,
            remote_api_token=remote_token,
            wake_endpoint_url=wake_url,
            wake_endpoint_token=wake_token,
            request_timeout_seconds=float(os.getenv("REQUEST_TIMEOUT_SECONDS", "15")),
            confirm_ttl_seconds=int(os.getenv("CONFIRM_TTL_SECONDS", "45")),
        )


class ConfirmationStore:
    def __init__(self, ttl_seconds: int) -> None:
        self._ttl_seconds = ttl_seconds
        self._values: dict[tuple[int, str], float] = {}

    def issue(self, user_id: int, action: str) -> None:
        if action not in DESTRUCTIVE_ACTIONS:
            raise ValueError("Confirmation is only available for destructive actions.")
        self._values[(user_id, action)] = time.monotonic() + self._ttl_seconds

    def consume(self, user_id: int, action: str) -> bool:
        expires_at = self._values.pop((user_id, action), 0)
        return expires_at >= time.monotonic()


def format_response(action: str, payload: Any) -> str:
    if not isinstance(payload, dict):
        return "Команда выполнена."
    if payload.get("ok") is False:
        return "Команда не выполнена."

    data = payload.get("data")
    if action == "status" and isinstance(data, dict):
        processes = data.get("codexProcesses")
        active = data.get("activeProfile") or "не выбран"
        count = data.get("profileCount", 0)
        running = isinstance(processes, list) and len(processes) > 0
        return (
            "Статус ПК\n"
            f"Codex: {'запущен' if running else 'не запущен'}\n"
            f"Активный профиль: {active}\n"
            f"Профилей: {count}"
        )
    if action == "accounts":
        return format_accounts(data)
    if action == "limits":
        return format_limits(data)
    if action in ACTION_SUCCESS_MESSAGES:
        return ACTION_SUCCESS_MESSAGES[action]

    message = payload.get("message")
    return message[:3500] if isinstance(message, str) and message else "Команда выполнена."


def format_accounts(data: Any) -> str:
    if not isinstance(data, list) or not data:
        return "Сохраненных аккаунтов пока нет."
    lines = ["Аккаунты Codex:"]
    for index, item in enumerate(data, 1):
        if not isinstance(item, dict):
            continue
        name = item.get("displayName") or item.get("name") or f"#{index}"
        marker = " (активный)" if item.get("active") else ""
        lines.append(f"{index}. {name}{marker}")
    return "\n".join(lines)[:3500]


def format_limits(data: Any) -> str:
    if not isinstance(data, list) or not data:
        return "Данных о лимитах пока нет."
    lines = ["Лимиты Codex:"]
    for item in data:
        if not isinstance(item, dict):
            continue
        name = item.get("displayName") or item.get("name") or "Аккаунт"
        if not item.get("success"):
            lines.append(f"{name}: ошибка")
            continue
        five_hour = _format_window("5 ч", item.get("fiveHour"))
        weekly = _format_window("Неделя", item.get("weekly"))
        lines.append(f"{name}: {five_hour}; {weekly}")
    return "\n".join(lines)[:3500]


def _format_window(label: str, value: Any) -> str:
    if not isinstance(value, dict):
        return f"{label}: нет данных"
    percent = value.get("percentLeft")
    percent_text = f"{round(float(percent)):g}%" if isinstance(percent, (int, float)) else "нет данных"
    reset = _format_reset(value.get("resetAt"))
    return f"{label}: {percent_text}" + (f", сброс {reset}" if reset else "")


def _format_reset(value: Any) -> str:
    if not isinstance(value, str) or not value:
        return ""
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            parsed = parsed.replace(tzinfo=ZoneInfo("UTC"))
        return parsed.astimezone(ZoneInfo("Europe/Moscow")).strftime("%d.%m %H:%M")
    except ValueError:
        return ""


def _required(name: str) -> str:
    value = os.getenv(name, "").strip()
    if not value:
        raise RuntimeError(f"{name} is required.")
    return value


def _https_url(value: str, *, allow_loopback_http: bool) -> str:
    parsed = urlparse(value)
    loopback = parsed.hostname in {"localhost", "127.0.0.1", "::1"}
    if parsed.username or parsed.password or parsed.fragment:
        raise RuntimeError("Service URL cannot contain credentials or a fragment.")
    if parsed.scheme != "https" and not (allow_loopback_http and parsed.scheme == "http" and loopback):
        raise RuntimeError("Service URL must use HTTPS (loopback HTTP is allowed only for local development).")
    if not parsed.hostname:
        raise RuntimeError("Service URL must include a host.")
    return value.rstrip("/")
