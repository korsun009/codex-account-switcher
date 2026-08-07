import os
from unittest.mock import patch

import pytest

from bot_core import ACTION_ROUTES, ConfirmationStore, Settings, format_accounts, format_limits, format_response


def test_configuration_has_no_default_owner_or_token():
    with patch.dict(os.environ, {}, clear=True):
        with pytest.raises(RuntimeError):
            Settings.from_environment()


def test_routes_match_remote_api_v2():
    assert ACTION_ROUTES["status"] == ("GET", "/status")
    assert ACTION_ROUTES["limits"] == ("GET", "/limits")
    assert ACTION_ROUTES["vpn_proxy"] == ("POST", "/v2ray/proxy")
    assert ACTION_ROUTES["sleep"] == ("POST", "/sleep")


def test_destructive_confirmation_is_one_time():
    store = ConfirmationStore(45)
    store.issue(123, "shutdown")
    assert store.consume(123, "shutdown") is True
    assert store.consume(123, "shutdown") is False


def test_accounts_use_display_names_and_mark_active_profile():
    text = format_accounts([
        {"name": "profile-1", "displayName": "Work", "active": True},
        {"name": "profile-2", "displayName": "Personal", "active": False},
    ])
    assert text == "Аккаунты Codex:\n1. Work (активный)\n2. Personal"
    assert "profile-1" not in text


def test_limits_are_compact_and_hide_error_details():
    text = format_limits([
        {
            "name": "profile-1",
            "displayName": "Work",
            "success": True,
            "fiveHour": {"percentLeft": 11, "resetAt": "2026-06-27T06:56:00Z"},
            "weekly": {"percentLeft": 39, "resetAt": "2026-07-02T02:16:00Z"},
            "message": "sensitive backend detail",
        },
        {
            "displayName": "Test",
            "success": False,
            "message": "Codex rejected a current token",
        },
    ])
    assert "Work: 5 ч: 11%, сброс 27.06 09:56; Неделя: 39%, сброс 02.07 05:16" in text
    assert "Test: ошибка" in text
    assert "token" not in text
    assert "sensitive" not in text


def test_status_is_rendered_in_russian():
    text = format_response("status", {
        "ok": True,
        "data": {"activeProfile": "profile-1", "profileCount": 3, "codexProcesses": [{"processId": 1}]},
    })
    assert "Codex: запущен" in text
    assert "Профилей: 3" in text
