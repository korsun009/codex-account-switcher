using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CodexControlDeck.Core;
using CodexControlDeck.Services;

namespace CodexControlDeck.ViewModels;

public partial class MainPageViewModel : ObservableObject, IDisposable
{
    private readonly DeckActionStore _actionStore = new();
    private readonly DeckActionExecutor _executor = new();
    private readonly DeckSettingsStore _settingsStore = new();
    private readonly RemoteApiClient _api = new();

    private string _statusMessage = "Подключение к Windows API...";
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    private string _apiStatus = "Не проверено";
    public string ApiStatus { get => _apiStatus; set => SetProperty(ref _apiStatus, value); }

    private string _syncStatus = "Не проверено";
    public string SyncStatus { get => _syncStatus; set => SetProperty(ref _syncStatus, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    private string _activeProfile = "Неизвестно";
    public string ActiveProfile { get => _activeProfile; set => SetProperty(ref _activeProfile, value); }

    public ObservableCollection<DeckTileViewModel> Tiles { get; } = [];
    public ObservableCollection<AccountViewModel> Accounts { get; } = [];
    public ObservableCollection<UsageViewModel> Limits { get; } = [];
    public ObservableCollection<ActionListItemViewModel> Actions { get; } = [];

    public string ApiBaseUrl => _api.BaseUrl;
    public bool HasApiToken => _api.HasToken;

    public MainPageViewModel()
    {
        AddBuiltInTiles();
    }

    public async Task InitializeAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        if (!string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
        {
            _api.SetBaseUrl(settings.ApiBaseUrl);
        }

        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await LoadLocalActionsAsync();
            var health = await _api.HealthAsync();
            ApiStatus = health.Ok && health.Data?.Healthy == true ? "Windows API доступен" : health.Error ?? "Windows API недоступен";
            if (!health.Ok)
            {
                StatusMessage = ApiStatus;
                SyncStatus = "Синхронизация ожидает Windows API";
                return;
            }

            var status = await _api.StatusAsync();
            if (status.Ok && status.Data is not null)
            {
                ActiveProfile = status.Data.ActiveProfile ?? "Не выбран";
                var codexState = status.Data.CodexProcesses.Count > 0 ? "запущен" : "не запущен";
                StatusMessage = $"Codex {codexState}, профиль: {ActiveProfile}";
            }
            else
            {
                StatusMessage = status.Error ?? "Не удалось получить статус Codex.";
            }

            await LoadAccountsAsync();
            await LoadLimitsAsync();
            var actions = await _api.ActionsAsync();
            SyncStatus = actions.Ok
                ? $"Telegram и Алиса видят {actions.Data?.Count ?? 0} удалённых сценариев"
                : actions.Error ?? "Не удалось проверить синхронизацию";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string> RunTileAsync(string id)
    {
        if (id.StartsWith("custom:", StringComparison.Ordinal))
        {
            var actionId = id[7..];
            var action = (await _actionStore.LoadAsync()).FirstOrDefault(item => item.Id == actionId)
                ?? throw new InvalidOperationException("Сценарий больше не существует.");
            var result = await _executor.ExecuteAsync(action);
            StatusMessage = result.Message;
            return result.Message;
        }

        var routes = new Dictionary<string, (string Method, string Path)>(StringComparer.Ordinal)
        {
            ["codex-start"] = ("POST", "/start-codex"),
            ["codex-stop"] = ("POST", "/stop-codex"),
            ["vpn-proxy"] = ("POST", "/v2ray/proxy"),
            ["vpn-restart"] = ("POST", "/v2ray/restart"),
            ["sleep"] = ("POST", "/sleep"),
            ["reboot"] = ("POST", "/reboot"),
            ["shutdown"] = ("POST", "/shutdown")
        };
        if (id is "refresh" or "limits" or "accounts" or "status")
        {
            await RefreshAsync();
            return StatusMessage;
        }
        if (!routes.TryGetValue(id, out var route))
        {
            throw new InvalidOperationException("Неизвестное действие.");
        }

        var response = await _api.ExecuteAsync(route.Method, route.Path, new { });
        StatusMessage = response.Ok ? response.Message : response.Error ?? "Команда не выполнена.";
        return StatusMessage;
    }

    public async Task<string> SwitchAccountAsync(string account)
    {
        var response = await _api.ExecuteAsync("POST", "/switch-account", new { account });
        StatusMessage = response.Ok ? "Аккаунт Codex переключён." : response.Error ?? "Не удалось переключить аккаунт.";
        await RefreshAsync();
        return StatusMessage;
    }

    public async Task SaveActionAsync(DeckActionDefinition action)
    {
        DeckActionValidator.EnsureValid(action);
        var actions = (await _actionStore.LoadAsync()).ToList();
        var index = actions.FindIndex(item => string.Equals(item.Id, action.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            actions[index] = action;
        }
        else
        {
            actions.Add(action);
        }
        await _actionStore.SaveAsync(actions);
        await LoadLocalActionsAsync();
        StatusMessage = $"Сценарий «{action.Name}» сохранён и доступен каналам без перезапуска.";
    }

    public async Task DeleteActionAsync(string id)
    {
        var actions = (await _actionStore.LoadAsync()).Where(item => !string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        await _actionStore.SaveAsync(actions);
        await LoadLocalActionsAsync();
        StatusMessage = "Сценарий удалён.";
    }

    public async Task<DeckActionDefinition?> FindActionAsync(string id) =>
        (await _actionStore.LoadAsync()).FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

    public async Task TestActionAsync(DeckActionDefinition action)
    {
        var localOnly = action with { AllowRemote = false, RequiresConfirmation = false, RemoteAccessAcknowledged = false };
        var result = await _executor.ExecuteAsync(localOnly);
        StatusMessage = result.Message;
    }

    public async Task<string> SaveConnectionAsync(string baseUrl)
    {
        _api.SetBaseUrl(baseUrl);
        await _settingsStore.SaveAsync(new DeckSettings(_api.BaseUrl));
        await RefreshAsync();
        return ApiStatus;
    }

    private async Task LoadLocalActionsAsync()
    {
        var customTiles = Tiles.Where(tile => tile.IsCustom).ToArray();
        foreach (var tile in customTiles)
        {
            Tiles.Remove(tile);
        }
        Actions.Clear();
        foreach (var action in await _actionStore.LoadAsync())
        {
            Actions.Add(new ActionListItemViewModel(action.Id, action.Name, KindName(action.Kind), action.AllowRemote));
            Tiles.Add(new DeckTileViewModel($"custom:{action.Id}", action.Name, action.Description, "\uE768", action.RequiresConfirmation, true));
        }
    }

    private async Task LoadAccountsAsync()
    {
        var response = await _api.AccountsAsync();
        Accounts.Clear();
        if (response.Ok && response.Data is not null)
        {
            foreach (var account in response.Data)
            {
                Accounts.Add(new AccountViewModel(account.Name, account.DisplayName, account.Active));
            }
        }
    }

    private async Task LoadLimitsAsync()
    {
        var response = await _api.LimitsAsync();
        Limits.Clear();
        if (!response.Ok || response.Data is null)
        {
            return;
        }
        foreach (var usage in response.Data)
        {
            if (!usage.Success)
            {
                Limits.Add(new UsageViewModel(usage.DisplayName, "Ошибка", "", ""));
                continue;
            }
            foreach (var window in usage.Windows)
            {
                var percent = window.PercentLeft is null ? "-" : $"{window.PercentLeft:0.#}%";
                var reset = window.ResetAt?.ToLocalTime().ToString("dd.MM HH:mm") ?? "без даты сброса";
                Limits.Add(new UsageViewModel(usage.DisplayName, window.Label, percent, reset));
            }
        }
    }

    private void AddBuiltInTiles()
    {
        Tiles.Add(new("status", "Обновить всё", "Статус ПК, Codex, аккаунтов и лимитов", "\uE72C"));
        Tiles.Add(new("codex-start", "Запустить Codex", "Открыть Codex на этом компьютере", "\uE768"));
        Tiles.Add(new("codex-stop", "Закрыть Codex", "Корректно остановить процессы Codex", "\uE711", true));
        Tiles.Add(new("vpn-proxy", "Включить VPN Proxy", "Перевести V2RayTun в Proxy Mode", "\uE774"));
        Tiles.Add(new("vpn-restart", "Перезапустить VPN", "Перезапустить V2RayTun и Proxy Mode", "\uE777", true));
        Tiles.Add(new("sleep", "Сон", "Перевести компьютер в настоящий сон", "\uE708", true));
        Tiles.Add(new("reboot", "Перезагрузка", "Перезагрузить Windows", "\uE777", true));
        Tiles.Add(new("shutdown", "Выключение", "Полностью выключить компьютер", "\uE7E8", true));
    }

    public static string KindName(DeckActionKind kind) => kind switch
    {
        DeckActionKind.Program => "Программа",
        DeckActionKind.Url => "Сайт",
        DeckActionKind.PowerShellScript => "PowerShell-файл",
        DeckActionKind.PowerShellCommand => "Команда PowerShell",
        _ => kind.ToString()
    };

    public void Dispose() => _api.Dispose();
}
