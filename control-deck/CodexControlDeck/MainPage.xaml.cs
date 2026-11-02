using CodexControlDeck.Core;
using CodexControlDeck.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace CodexControlDeck;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApiUrlBox.Text = ViewModel.ApiBaseUrl;
        TokenStatusText.Text = ViewModel.HasApiToken ? "Настроен, значение скрыто" : "Не настроен";
        await RunUiTask(ViewModel.InitializeAsync);
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer?.Tag as string) ?? "deck";
        DeckPanel.Visibility = tag == "deck" ? Visibility.Visible : Visibility.Collapsed;
        ActionsPanel.Visibility = tag == "actions" ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsPanel.Visibility = tag == "connections" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunUiTask(ViewModel.RefreshAsync);

    private async void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }
        var tile = ViewModel.Tiles.FirstOrDefault(item => item.Id == id);
        if (tile?.RequiresConfirmation == true && !await ConfirmAsync(tile.Name, tile.Description))
        {
            return;
        }
        await RunUiTask(() => ViewModel.RunTileAsync(id));
    }

    private async void Account_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string account })
        {
            return;
        }
        var displayName = ViewModel.Accounts.FirstOrDefault(item => item.Name == account)?.DisplayName ?? account;
        if (await ConfirmAsync("Переключить аккаунт", $"Codex будет переключён на «{displayName}»."))
        {
            await RunUiTask(() => ViewModel.SwitchAccountAsync(account));
        }
    }

    private void NewAction_Click(object sender, RoutedEventArgs e)
    {
        ActionsList.SelectedItem = null;
        ClearEditor();
        ActionIdBox.Focus(FocusState.Programmatic);
    }

    private async void Action_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionsList.SelectedItem is not ActionListItemViewModel selected)
        {
            return;
        }
        var action = await ViewModel.FindActionAsync(selected.Id);
        if (action is not null)
        {
            PopulateEditor(action);
        }
    }

    private void ActionKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BrowseButton is null || AdvancedWarning is null)
        {
            return;
        }
        var kind = SelectedKind();
        BrowseButton.Visibility = kind is DeckActionKind.Program or DeckActionKind.PowerShellScript ? Visibility.Visible : Visibility.Collapsed;
        AdvancedWarning.IsOpen = kind == DeckActionKind.PowerShellCommand;
    }

    private void AllowRemote_Toggled(object sender, RoutedEventArgs e)
    {
        ConfirmationCheck.IsChecked = AllowRemoteToggle.IsOn;
        ConfirmationCheck.IsEnabled = !AllowRemoteToggle.IsOn;
        RemoteAcknowledgeCheck.Visibility = AllowRemoteToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        if (!AllowRemoteToggle.IsOn)
        {
            RemoteAcknowledgeCheck.IsChecked = false;
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
        if (SelectedKind() == DeckActionKind.Program)
        {
            picker.FileTypeFilter.Add(".exe");
        }
        else
        {
            picker.FileTypeFilter.Add(".ps1");
        }
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ActionTargetBox.Text = file.Path;
        }
    }

    private async void SaveAction_Click(object sender, RoutedEventArgs e)
    {
        await RunEditorTask(async () =>
        {
            await ViewModel.SaveActionAsync(ReadEditor());
            EditorMessage.Severity = InfoBarSeverity.Success;
            EditorMessage.Message = ViewModel.StatusMessage;
            EditorMessage.IsOpen = true;
        });
    }

    private async void TestAction_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync("Проверить сценарий", "Сценарий будет запущен один раз на этом компьютере."))
        {
            await RunEditorTask(() => ViewModel.TestActionAsync(ReadEditor()));
        }
    }

    private async void DeleteAction_Click(object sender, RoutedEventArgs e)
    {
        var id = ActionIdBox.Text.Trim();
        if (id.Length == 0 || !await ConfirmAsync("Удалить сценарий", "Кнопка также исчезнет из Telegram и Алисы."))
        {
            return;
        }
        await RunEditorTask(() => ViewModel.DeleteActionAsync(id));
        ClearEditor();
    }

    private async void SaveConnection_Click(object sender, RoutedEventArgs e)
    {
        await RunUiTask(() => ViewModel.SaveConnectionAsync(ApiUrlBox.Text));
        ApiUrlBox.Text = ViewModel.ApiBaseUrl;
    }

    private DeckActionDefinition ReadEditor()
    {
        var aliases = AliasLines(ActionAliasesBox.Text);
        return new DeckActionDefinition
        {
            Id = ActionIdBox.Text.Trim().ToLowerInvariant(),
            Name = ActionNameBox.Text.Trim(),
            Description = ActionDescriptionBox.Text.Trim(),
            Kind = SelectedKind(),
            Target = ActionTargetBox.Text.Trim(),
            Arguments = Lines(ActionArgumentsBox.Text),
            Aliases = aliases,
            AllowRemote = AllowRemoteToggle.IsOn,
            RequiresConfirmation = ConfirmationCheck.IsChecked == true,
            RemoteAccessAcknowledged = RemoteAcknowledgeCheck.IsChecked == true,
            Order = ActionsList.SelectedIndex < 0 ? ViewModel.Actions.Count : ActionsList.SelectedIndex
        };
    }

    private void PopulateEditor(DeckActionDefinition action)
    {
        ActionIdBox.Text = action.Id;
        ActionNameBox.Text = action.Name;
        ActionDescriptionBox.Text = action.Description;
        ActionKindBox.SelectedIndex = (int)action.Kind;
        ActionTargetBox.Text = action.Target;
        ActionArgumentsBox.Text = string.Join(Environment.NewLine, action.Arguments);
        ActionAliasesBox.Text = string.Join(Environment.NewLine, action.Aliases);
        AllowRemoteToggle.IsOn = action.AllowRemote;
        ConfirmationCheck.IsChecked = action.RequiresConfirmation;
        RemoteAcknowledgeCheck.IsChecked = action.RemoteAccessAcknowledged;
    }

    private void ClearEditor()
    {
        ActionIdBox.Text = "";
        ActionNameBox.Text = "";
        ActionDescriptionBox.Text = "";
        ActionKindBox.SelectedIndex = 0;
        ActionTargetBox.Text = "";
        ActionArgumentsBox.Text = "";
        ActionAliasesBox.Text = "";
        AllowRemoteToggle.IsOn = false;
        ConfirmationCheck.IsChecked = false;
        RemoteAcknowledgeCheck.IsChecked = false;
        EditorMessage.IsOpen = false;
    }

    private DeckActionKind SelectedKind()
    {
        var tag = (ActionKindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return Enum.TryParse<DeckActionKind>(tag, out var kind) ? kind : DeckActionKind.Program;
    }

    private static string[] Lines(string text) => text
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] AliasLines(string text) => text
        .Split(["\r\n", "\n", ";", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = "Подтвердить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task RunEditorTask(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            EditorMessage.Severity = InfoBarSeverity.Error;
            EditorMessage.Message = error.Message;
            EditorMessage.IsOpen = true;
        }
    }

    private async Task RunUiTask(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            ViewModel.StatusMessage = error.Message;
        }
    }
}
