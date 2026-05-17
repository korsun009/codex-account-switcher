using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using AAlert = AntdUI.Alert;
using AButton = AntdUI.Button;
using AInput = AntdUI.Input;
using ALabel = AntdUI.Label;
using APanel = AntdUI.Panel;
using ATag = AntdUI.Tag;
using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher;

public sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly CodexHomeLayout _layout;
    private readonly AccountSwitcherService _switcher;
    private readonly CodexUsageService _usageService = new();
    private readonly SqliteAppDatabase? _database;
    private readonly string _settingsPath;
    private readonly TableLayoutPanel _shell;
    private readonly TableLayoutPanel _main;
    private readonly FlowLayoutPanel _cards;
    private readonly FlowLayoutPanel _sidebarActions;
    private readonly APanel _sidebar;
    private readonly APanel _settingsPanel;
    private readonly FlowLayoutPanel _profileSettingsList;
    private readonly ALabel _activeLabel;
    private readonly ALabel _themeCaption;
    private readonly ALabel _languageCaption;
    private readonly AInput _newProfileInput;
    private readonly ALabel _log;
    private readonly System.Windows.Forms.Timer _resizeTimer;
    private SwitcherSettings _settings;
    private UiPalette _palette;

    public MainForm(CodexHomeLayout layout, AccountSwitcherService switcher, SqliteAppDatabase? database = null)
    {
        _layout = layout;
        _switcher = switcher;
        _database = database;
        _settingsPath = Path.Combine(_layout.ProfilesDirectory, "switcher-settings.json");
        _settings = LoadSettings();
        _palette = ResolvePalette();

        Text = T("app.title");
        Icon = LoadAppIcon();
        MinimumSize = new Size(1180, 720);
        Size = new Size(1380, 820);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        Font = new Font("Segoe UI Variable Text", 9F);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        AntdUI.Config.Animation = false;
        AntdUI.Config.ShadowEnabled = false;

        _resizeTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            ApplyResponsiveLayout();
        };

        var menu = BuildMenu();
        MainMenuStrip = menu;
        Controls.Add(menu);

        _shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 24, 28, 28),
            ColumnCount = 3,
            RowCount = 1
        };
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        Controls.Add(_shell);

        _sidebarActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        _sidebar = BuildSidebar();
        _shell.Controls.Add(_sidebar, 0, 0);

        _main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(26, 0, 0, 0),
            ColumnCount = 1,
            RowCount = 3
        };
        _main.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        _main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _main.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
        _shell.Controls.Add(_main, 1, 0);

        _activeLabel = new ALabel
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 17F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _main.Controls.Add(_activeLabel, 0, 0);

        _cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            Padding = new Padding(0, 6, 12, 8),
            Margin = new Padding(0)
        };
        _main.Controls.Add(_cards, 0, 1);

        var logCard = BuildLogCard();
        _main.Controls.Add(logCard, 0, 2);
        _log = logCard.Controls.Find("EventLog", searchAllChildren: true).OfType<ALabel>().FirstOrDefault()
            ?? throw new InvalidOperationException(T("log.createFailed"));

        _profileSettingsList = new FlowLayoutPanel
        {
            Width = 360,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        _newProfileInput = new AInput
        {
            Dock = DockStyle.Top,
            Height = 46,
            Radius = 10,
            PlaceholderText = T("settings.profilePlaceholder")
        };
        _languageCaption = new ALabel { Dock = DockStyle.Top, Height = 28 };
        _themeCaption = new ALabel { Dock = DockStyle.Top, Height = 28 };
        _settingsPanel = BuildSettingsPanel();
        _shell.Controls.Add(_settingsPanel, 2, 0);

        Resize += (_, _) => ScheduleResponsiveLayout();
        ResizeEnd += (_, _) => ApplyResponsiveLayout();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        FormClosed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _resizeTimer.Dispose();
        };

        ApplyTheme(rebuild: false);
        RefreshStatus();
        ApplyResponsiveLayout();
        AppendLog(T("log.ready"));
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(12, 4, 12, 4),
            Height = 30,
            RenderMode = ToolStripRenderMode.Professional,
            Renderer = new AppMenuRenderer(() => _palette)
        };
        var app = new ToolStripMenuItem(T("menu.main"));
        app.DropDownItems.Add(T("menu.instructions"), null, (_, _) => ShowInstructionDialog());
        app.DropDownItems.Add(T("menu.limits"), null, (_, _) => ShowLimitsDialog());
        app.DropDownItems.Add(T("menu.settings"), null, (_, _) => ToggleSettingsPanel());
        app.DropDownItems.Add(new ToolStripSeparator());
        app.DropDownItems.Add(T("menu.exit"), null, (_, _) => Close());

        var actions = new ToolStripMenuItem(T("menu.service"));
        actions.DropDownItems.Add(T("menu.addAccount"), null, (_, _) => ShowAddAccountWizard());
        actions.DropDownItems.Add(new ToolStripSeparator());
        actions.DropDownItems.Add(T("menu.cleanLogin"), null, async (_, _) => await PrepareCleanLoginAsync());
        actions.DropDownItems.Add(T("menu.inventory"), null, async (_, _) => await InventoryAsync());
        actions.DropDownItems.Add(T("menu.backup"), null, async (_, _) => await BackupAsync());
        actions.DropDownItems.Add(T("menu.restore"), null, async (_, _) => await RollbackAsync());
        actions.DropDownItems.Add(T("menu.fileAuth"), null, async (_, _) => await EnsureFileAuthAsync());

        menu.Items.Add(app);
        menu.Items.Add(actions);
        ConfigureDropDown(app);
        ConfigureDropDown(actions);
        return menu;
    }

    private void ConfigureDropDown(ToolStripMenuItem item)
    {
        item.DropDown.RenderMode = ToolStripRenderMode.Professional;
        item.DropDown.Renderer = new AppMenuRenderer(() => _palette);
        item.DropDown.Padding = new Padding(0, 6, 0, 6);
        if (item.DropDown is ToolStripDropDownMenu menu)
        {
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
        }
    }

    private APanel BuildSidebar()
    {
        var side = new APanel
        {
            Dock = DockStyle.Fill,
            Radius = 18,
            BorderWidth = 1,
            Padding = new Padding(18),
            Shadow = 0,
            ShadowOpacity = 0F
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 136));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        side.Controls.Add(layout);

        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        brand.Controls.Add(new ALabel
        {
            Text = "Codex",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 19F, FontStyle.Bold)
        }, 0, 0);
        brand.Controls.Add(new ALabel
        {
            Text = T("app.subtitle"),
            Dock = DockStyle.Fill
        }, 0, 1);
        layout.Controls.Add(brand, 0, 0);

        layout.Controls.Add(BuildSafetyBox(), 0, 1);

        AddSidebarButton(T("sidebar.addAccount"), () =>
        {
            ShowAddAccountWizard();
            return Task.CompletedTask;
        }, AntdUI.TTypeMini.Primary);
        AddSidebarButton(T("sidebar.limits"), () =>
        {
            ShowLimitsDialog();
            return Task.CompletedTask;
        }, AntdUI.TTypeMini.Default);
        AddSidebarButton(T("sidebar.settings"), () =>
        {
            ToggleSettingsPanel();
            return Task.CompletedTask;
        }, AntdUI.TTypeMini.Default);
        layout.Controls.Add(_sidebarActions, 0, 3);

        layout.Controls.Add(new ALabel
        {
            Text = _layout.CodexHome,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextMultiLine = true
        }, 0, 4);
        return side;
    }

    private void AddSidebarButton(string text, Func<Task> action, AntdUI.TTypeMini type)
    {
        var button = new AButton
        {
            Text = text,
            Type = type,
            Radius = 12,
            Width = Math.Max(160, _sidebarActions.ClientSize.Width),
            Height = 46,
            Margin = new Padding(0, 0, 0, 12)
        };
        button.Click += async (_, _) => await action();
        _sidebarActions.Controls.Add(button);
        ScheduleResponsiveLayout();
    }

    private Control BuildSafetyBox()
    {
        var box = new APanel
        {
            Dock = DockStyle.Fill,
            Radius = 14,
            BorderWidth = 1,
            Padding = new Padding(16),
            Margin = new Padding(0, 6, 0, 10)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        box.Controls.Add(layout);
        layout.Controls.Add(new ALabel
        {
            Text = T("safety.title"),
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold)
        }, 0, 0);
        layout.Controls.Add(new ALabel
        {
            Text = T("safety.text"),
            Dock = DockStyle.Fill,
            TextMultiLine = true
        }, 0, 1);
        return box;
    }

    private APanel BuildLogCard()
    {
        var card = new APanel
        {
            Dock = DockStyle.Fill,
            Radius = 16,
            BorderWidth = 1,
            Padding = new Padding(16),
            Shadow = 0,
            ShadowOpacity = 0F
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);
        layout.Controls.Add(new ALabel
        {
            Text = T("log.title"),
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Text", 11F, FontStyle.Bold)
        }, 0, 0);

        var eventSurface = new APanel
        {
            Name = "EventSurface",
            Dock = DockStyle.Fill,
            Radius = 10,
            BorderWidth = 1,
            Padding = new Padding(14, 10, 14, 10),
            Shadow = 0,
            ShadowOpacity = 0F
        };
        eventSurface.Controls.Add(new ALabel
        {
            Name = "EventLog",
            Dock = DockStyle.Fill,
            TextMultiLine = true,
            Font = new Font("Segoe UI Variable Text", 9.5F),
            TextAlign = ContentAlignment.MiddleLeft
        });
        layout.Controls.Add(eventSurface, 0, 1);
        return card;
    }

    private APanel BuildSettingsPanel()
    {
        var panel = new APanel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            Radius = 18,
            BorderWidth = 1,
            Shadow = 0,
            ShadowOpacity = 0F,
            Margin = new Padding(22, 0, 0, 0),
            Padding = new Padding(18)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.Controls.Add(layout);

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 0, 8, 10),
            Margin = new Padding(0, 0, 0, 10),
            BackColor = _palette.Surface
        };
        content.HandleCreated += (_, _) => NativeTheme.ApplyControlTheme(content.Handle, _palette.IsDark);
        layout.Controls.Add(content, 0, 0);

        var title = new ALabel
        {
            Text = T("settings.title"),
            Height = 44,
            Font = new Font("Segoe UI Variable Display", 16F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        };
        content.Controls.Add(title);

        var themeBox = new TableLayoutPanel { ColumnCount = 1, RowCount = 5, Height = 224, Margin = new Padding(0, 0, 0, 12) };
        themeBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        themeBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        themeBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        themeBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        themeBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        themeBox.Controls.Add(_themeCaption, 0, 0);
        themeBox.Controls.Add(ThemeButton(T("theme.auto"), ThemeMode.Auto), 0, 1);
        themeBox.Controls.Add(ThemeButton(T("theme.dark"), ThemeMode.Dark), 0, 2);
        themeBox.Controls.Add(ThemeButton(T("theme.gray"), ThemeMode.Gray), 0, 3);
        themeBox.Controls.Add(ThemeButton(T("theme.light"), ThemeMode.Light), 0, 4);
        content.Controls.Add(themeBox);

        _languageCaption.Height = 28;
        _languageCaption.Margin = new Padding(0, 0, 0, 8);
        content.Controls.Add(_languageCaption);

        var languageBox = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Height = 144, Margin = new Padding(0, 0, 0, 18) };
        languageBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        languageBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        languageBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        var languageIndex = 0;
        foreach (var option in Localizer.Options)
        {
            languageBox.Controls.Add(LanguageButton(option), 0, languageIndex++);
        }
        content.Controls.Add(languageBox);

        content.Controls.Add(new ALabel
        {
            Text = T("settings.profiles"),
            Height = 32,
            Font = new Font("Segoe UI Variable Text", 11F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        });

        var addBox = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Height = 58, Margin = new Padding(0, 0, 0, 18) };
        addBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        addBox.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        addBox.Controls.Add(_newProfileInput, 0, 0);
        var addButton = new AButton
        {
            Text = T("settings.add"),
            Dock = DockStyle.Fill,
            Radius = 10,
            Type = AntdUI.TTypeMini.Primary,
            Margin = new Padding(12, 0, 0, 18)
        };
        addButton.Click += (_, _) => AddProfileFromSettings();
        addBox.Controls.Add(addButton, 1, 0);
        content.Controls.Add(addBox);
        content.Controls.Add(_profileSettingsList);

        void ResizeContent()
        {
            var width = Math.Max(320, content.ClientSize.Width - 12);
            foreach (Control child in content.Controls)
            {
                if (child.Width != width)
                {
                    child.Width = width;
                }
            }

            ResizeProfileSettingsRows(width);
        }

        content.Resize += (_, _) => ResizeContent();
        panel.VisibleChanged += (_, _) => ResizeContent();

        var closeButton = new AButton
        {
            Text = T("settings.close"),
            Dock = DockStyle.Fill,
            Radius = 10,
            Type = AntdUI.TTypeMini.Default
        };
        closeButton.Click += (_, _) => ToggleSettingsPanel();
        layout.Controls.Add(closeButton, 0, 1);
        return panel;
    }

    private AButton ThemeButton(string text, ThemeMode mode)
    {
        var button = new AButton
        {
            Text = text,
            Dock = DockStyle.Fill,
            Radius = 12,
            Type = _settings.Theme == mode ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default,
            Margin = new Padding(0, 0, 0, 10)
        };
        button.Click += (_, _) =>
        {
            _settings = _settings with { Theme = mode };
            SaveSettings();
            ApplyTheme(rebuild: true);
        };
        return button;
    }

    private AButton LanguageButton(LanguageOption option)
    {
        var button = new AButton
        {
            Text = option.DisplayName,
            Dock = DockStyle.Fill,
            Radius = 12,
            Type = _settings.Language == option.Language ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default,
            Margin = new Padding(0, 0, 0, 10)
        };
        button.Click += (_, _) =>
        {
            if (_settings.Language == option.Language)
            {
                return;
            }

            _settings = _settings with { Language = option.Language };
            SaveSettings();
            Application.Restart();
            Close();
        };
        return button;
    }

    private Control AccountCard(AccountProfile profile, string? activeProfile)
    {
        var active = string.Equals(profile.Name, activeProfile, StringComparison.OrdinalIgnoreCase);
        var ready = profile.HasAuthJson;
        var statusText = active ? T("card.active") : ready ? T("card.ready") : T("card.empty");

        var card = new APanel
        {
            Width = CurrentCardWidth(),
            Height = 282,
            Margin = new Padding(0, 0, 22, 22),
            Radius = 18,
            Shadow = 0,
            ShadowOpacity = 0F,
            BorderWidth = active ? 2 : 1,
            Padding = new Padding(18)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 6));
        card.Controls.Add(layout);

        layout.Controls.Add(new ATag
        {
            Text = statusText,
            Tag = active ? "active" : ready ? "ready" : "empty",
            Radius = 10,
            Size = new Size(118, 32),
            Type = active ? AntdUI.TTypeMini.Primary : ready ? AntdUI.TTypeMini.Info : AntdUI.TTypeMini.Default,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        layout.Controls.Add(new ALabel
        {
            Text = profile.DisplayName,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 18F, FontStyle.Bold),
            AutoEllipsis = true
        }, 0, 1);
        layout.Controls.Add(new ALabel
        {
            Text = active
                ? T("card.activeText")
                : ready
                    ? T("card.readyText")
                    : T("card.emptyText"),
            Dock = DockStyle.Fill,
            TextMultiLine = true
        }, 0, 2);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        var switchButton = new AButton
        {
            Text = active ? T("card.open") : T("card.switch"),
            Enabled = !active,
            Type = active ? AntdUI.TTypeMini.Default : AntdUI.TTypeMini.Primary,
            Radius = 10,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 0)
        };
        switchButton.Click += async (_, _) => await SwitchAsync(profile.Name);
        buttons.Controls.Add(switchButton, 0, 0);

        var captureButton = new AButton
        {
            Text = T("card.saveLogin"),
            Type = AntdUI.TTypeMini.Info,
            Radius = 10,
            Dock = DockStyle.Fill
        };
        captureButton.Click += async (_, _) => await CaptureAsync(profile.Name);
        buttons.Controls.Add(captureButton, 1, 0);
        layout.Controls.Add(buttons, 0, 3);

        layout.Controls.Add(new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = active ? _palette.Accent : _palette.Line
        }, 0, 4);

        ApplyThemeToControls(card);
        StyleCard(card, active);
        return card;
    }

    private void AddProfileFromSettings()
    {
        RunSync(() =>
        {
            var profile = _switcher.AddProfile(_newProfileInput.Text);
            _newProfileInput.Text = "";
            AppendLog(F("profile.added", profile.DisplayName));
            RefreshStatus();
        });
    }

    private void DeleteProfileFromSettings(AccountProfile profile)
    {
        RunSync(() =>
        {
            var confirm = MessageBox.Show(
                F("profile.deleteConfirm", profile.DisplayName),
                T("profile.deleteTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            var result = _switcher.DeleteProfile(profile.Name);
            AppendLog(result.Message);
            RefreshStatus();
        });
    }

    private void ToggleSettingsPanel()
    {
        _settingsPanel.Visible = !_settingsPanel.Visible;
        _shell.ColumnStyles[2].Width = _settingsPanel.Visible ? 430 : 0;
        RefreshProfileSettings();
    }

    private async Task SwitchAsync(string profileName) => await RunAsync(async () =>
    {
        var result = await _switcher.SwitchToAsync(profileName, CancellationToken.None);
        AppendLog(result.Message);
        RefreshStatus();
    });

    private async Task CaptureAsync(string profileName) => await RunAsync(async () =>
    {
        var result = await _switcher.CaptureCurrentAuthAsProfileAsync(profileName, CancellationToken.None);
        AppendLog(result.Message);
        RefreshStatus();
    });

    private async Task PrepareCleanLoginAsync() => await RunAsync(async () =>
    {
        var result = await _switcher.PrepareCleanLoginAsync(CancellationToken.None);
        AppendLog(result.Message);
        RefreshStatus();
    });

    private async Task BackupAsync() => await RunAsync(async () =>
    {
        var backup = await _switcher.CreateAccountFileBackupAsync(CancellationToken.None);
        AppendLog(F("backup.created", backup));
    });

    private async Task RollbackAsync() => await RunAsync(async () =>
    {
        var result = await _switcher.RestoreLatestAuthBackupAsync(CancellationToken.None);
        AppendLog(result.Message);
        RefreshStatus();
    });

    private async Task InventoryAsync() => await RunAsync(async () =>
    {
        var report = await _switcher.WriteInventoryReportAsync(CancellationToken.None);
        AppendLog(F("inventory.written", report));
    });

    private async Task EnsureFileAuthAsync() => await RunAsync(async () =>
    {
        var result = await _switcher.EnsureFileAuthConfigAsync(CancellationToken.None);
        AppendLog(result);
    });

    private void ShowAddAccountWizard()
    {
        using var dialog = CreateScreen(T("wizard.title"), new Size(740, 600), new Size(640, 500));
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = _palette.Page
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        dialog.Controls.Add(root);

        root.Controls.Add(new ALabel
        {
            Text = T("wizard.header"),
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 17F, FontStyle.Bold),
            ForeColor = _palette.Text
        }, 0, 0);

        root.Controls.Add(new ALabel
        {
            Text = T("wizard.description"),
            Dock = DockStyle.Fill,
            TextMultiLine = true,
            ForeColor = _palette.Muted
        }, 0, 1);

        var nameBox = new AInput
        {
            Dock = DockStyle.Fill,
            Height = 46,
            Radius = 10,
            PlaceholderText = T("wizard.placeholder")
        };
        nameBox.BackColor = _palette.Control;
        nameBox.ForeColor = _palette.Text;
        root.Controls.Add(nameBox, 0, 2);

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = _palette.Page,
            Margin = new Padding(0, 0, 0, 10)
        };
        content.HandleCreated += (_, _) => NativeTheme.ApplyControlTheme(content.Handle, _palette.IsDark);
        root.Controls.Add(content, 0, 3);

        var createStep = WizardStep(T("wizard.step1.title"), T("wizard.step1.text"));
        var loginStep = WizardStep(T("wizard.step2.title"), T("wizard.step2.text"));
        var saveStep = WizardStep(T("wizard.step3.title"), T("wizard.step3.text"));
        content.Controls.Add(createStep);
        content.Controls.Add(loginStep);
        content.Controls.Add(saveStep);

        var status = new ALabel
        {
            Text = T("wizard.start"),
            Width = 640,
            Height = 52,
            TextMultiLine = true,
            ForeColor = _palette.Muted,
            Margin = new Padding(2, 2, 10, 4)
        };
        content.Controls.Add(status);

        void ResizeWizardContent()
        {
            var width = Math.Max(560, content.ClientSize.Width - 24);
            foreach (Control control in content.Controls)
            {
                control.Width = width;
            }
        }

        content.Resize += (_, _) => ResizeWizardContent();
        dialog.Shown += (_, _) => ResizeWizardContent();

        var buttons = FooterButtons(dialog, T("common.close"));
        root.Controls.Add(buttons, 0, 4);
        var saveLogin = DialogButton(T("card.saveLogin"), _palette.Button, _palette.Text);
        var openLogin = DialogButton(T("wizard.openCodex"), _palette.Button, _palette.Text);
        var createProfile = DialogButton(T("wizard.createProfile"), _palette.Accent, _palette.ButtonText);
        saveLogin.Enabled = false;
        openLogin.Enabled = false;
        buttons.Controls.Add(saveLogin);
        buttons.Controls.Add(openLogin);
        buttons.Controls.Add(createProfile);

        AccountProfile? profile = null;

        createProfile.Click += (_, _) =>
        {
            try
            {
                profile = _switcher.AddProfile(nameBox.Text);
                nameBox.Enabled = false;
                createProfile.Enabled = false;
                openLogin.Enabled = true;
                status.Text = F("wizard.profileCreated", profile.DisplayName);
                AppendLog(F("wizard.profileCreatedLog", profile.DisplayName));
                RefreshStatus();
            }
            catch (Exception ex)
            {
                status.Text = F("wizard.createFailed", ex.Message);
            }
        };

        openLogin.Click += async (_, _) =>
        {
            if (profile is null)
            {
                status.Text = T("wizard.createFirst");
                return;
            }

            openLogin.Enabled = false;
            status.Text = T("wizard.preparing");
            try
            {
                UseWaitCursor = true;
                var result = await _switcher.PrepareCleanLoginAsync(CancellationToken.None);
                AppendLog(result.Message);
                RefreshStatus();
                saveLogin.Enabled = true;
                status.Text = T("wizard.opened");
            }
            catch (Exception ex)
            {
                openLogin.Enabled = true;
                status.Text = F("wizard.openFailed", ex.Message);
            }
            finally
            {
                UseWaitCursor = false;
            }
        };

        saveLogin.Click += async (_, _) =>
        {
            if (profile is null)
            {
                status.Text = T("wizard.createFirst");
                return;
            }

            saveLogin.Enabled = false;
            status.Text = T("wizard.saving");
            try
            {
                UseWaitCursor = true;
                var result = await _switcher.CaptureCurrentAuthAsProfileAsync(profile.Name, CancellationToken.None);
                AppendLog(result.Message);
                RefreshStatus();
                status.Text = result.Success
                    ? F("wizard.saved", profile.DisplayName)
                    : result.Message;
                if (!result.Success)
                {
                    saveLogin.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                saveLogin.Enabled = true;
                status.Text = F("wizard.saveFailed", ex.Message);
            }
            finally
            {
                UseWaitCursor = false;
            }
        };

        dialog.ShowDialog(this);
    }

    private APanel WizardStep(string title, string text)
    {
        var panel = new APanel
        {
            Width = 640,
            Height = 82,
            Radius = 14,
            BorderWidth = 1,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0, 0, 10, 10),
            Shadow = 0,
            ShadowOpacity = 0F
        };
        StylePanel(panel);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(layout);
        layout.Controls.Add(new ALabel
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold)
        }, 0, 0);
        layout.Controls.Add(new ALabel
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextMultiLine = true,
            ForeColor = _palette.Muted
        }, 0, 1);
        return panel;
    }

    private void ShowInstructionDialog()
    {
        using var dialog = CreateScreen(T("instructions.title"), new Size(760, 640), new Size(640, 520));
        var layout = ScreenLayout(dialog);

        layout.Controls.Add(new ALabel
        {
            Text = T("instructions.header"),
            Dock = DockStyle.Top,
            Height = 38,
            Font = new Font("Segoe UI Variable Display", 18F, FontStyle.Bold),
            ForeColor = _palette.Text
        });
        layout.Controls.Add(InstructionSection(
            T("instructions.idea.title"),
            T("instructions.idea.text"),
            112));
        layout.Controls.Add(InstructionSection(
            T("instructions.add.title"),
            T("instructions.add.text"),
            208));
        layout.Controls.Add(InstructionSection(
            T("instructions.master.title"),
            T("instructions.master.text"),
            150));
        layout.Controls.Add(InstructionSection(
            T("instructions.switch.title"),
            T("instructions.switch.text"),
            120));
        layout.Controls.Add(InstructionSection(
            T("instructions.buttons.title"),
            T("instructions.buttons.text"),
            184));
        layout.Controls.Add(InstructionSection(
            T("instructions.service.title"),
            T("instructions.service.text"),
            172));
        layout.Controls.Add(InstructionSection(
            T("instructions.rule.title"),
            T("instructions.rule.text"),
            126));

        AddScreenFooter(dialog, T("common.close"));
        dialog.ShowDialog(this);
    }

    private void ShowLimitsDialog()
    {
        using var dialog = CreateScreen(T("limits.title"), new Size(880, 560), new Size(760, 460));
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 6,
            BackColor = _palette.Page
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        dialog.Controls.Add(root);

        root.Controls.Add(new ALabel
        {
            Text = T("limits.header"),
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Display", 17F, FontStyle.Bold),
            ForeColor = _palette.Text
        }, 0, 0);
        root.Controls.Add(new ALabel
        {
            Text = T("limits.description"),
            Dock = DockStyle.Fill,
            TextMultiLine = true,
            ForeColor = _palette.Muted
        }, 0, 1);

        root.Controls.Add(UsageHeader(), 0, 2);

        var rowsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = _palette.Page,
            Margin = new Padding(0, 0, 0, 8)
        };
        rowsPanel.HandleCreated += (_, _) => NativeTheme.ApplyControlTheme(rowsPanel.Handle, _palette.IsDark);
        root.Controls.Add(rowsPanel, 0, 3);

        var active = _switcher.ReadActiveProfile();
        var usageRows = _switcher.ListProfiles()
            .Select(profile => CreateUsageRow(profile, active))
            .ToList();
        foreach (var usageRow in usageRows)
        {
            rowsPanel.Controls.Add(usageRow.Panel);
        }

        void ResizeRows()
        {
            var width = Math.Max(680, rowsPanel.ClientSize.Width - 24);
            foreach (var usageRow in usageRows)
            {
                usageRow.Panel.Width = width;
            }
        }

        rowsPanel.Resize += (_, _) => ResizeRows();
        dialog.Shown += (_, _) => ResizeRows();

        var status = new ALabel
        {
            Text = T("limits.refreshHint"),
            Dock = DockStyle.Fill,
            TextMultiLine = true,
            ForeColor = _palette.Muted,
            Margin = new Padding(0, 0, 0, 4)
        };
        root.Controls.Add(status, 0, 4);

        var buttons = FooterButtons(dialog, T("common.close"));
        root.Controls.Add(buttons, 0, 5);
        var refresh = DialogButton(T("limits.refresh"), _palette.Accent, _palette.ButtonText);
        refresh.Click += async (_, _) =>
        {
            refresh.Enabled = false;
            status.Text = T("limits.refreshing");
            try
            {
                var updated = 0;
                foreach (var usageRow in usageRows)
                {
                    SetUsageRowStatus(usageRow, T("limits.updating"));
                    if (!File.Exists(usageRow.AuthPath))
                    {
                        SetUsageRowResult(usageRow, null, null, T("limits.notSaved"));
                        continue;
                    }

                    var result = await _usageService.FetchAsync(usageRow.AuthPath, CancellationToken.None);
                    if (!result.Success)
                    {
                        SetUsageRowResult(usageRow, null, null, result.Message);
                        continue;
                    }

                    SetUsageRowResult(usageRow, result.FiveHour, result.Weekly, result.FetchedAt is null
                        ? T("limits.updated")
                        : T("limits.updated") + " " + result.FetchedAt.Value.ToLocalTime().ToString("HH:mm"));
                    updated++;
                }

                status.Text = F("limits.checked", usageRows.Count, updated);
            }
            catch (Exception ex)
            {
                status.Text = F("limits.failed", ex.Message);
            }
            finally
            {
                refresh.Enabled = true;
            }
        };
        buttons.Controls.Add(refresh);
        dialog.Shown += (_, _) => refresh.PerformClick();
        dialog.ShowDialog(this);
    }

    private Form CreateScreen(string title, Size size, Size minimumSize)
    {
        var dialog = new Form
        {
            Text = title,
            Icon = Icon,
            StartPosition = FormStartPosition.CenterParent,
            Size = size,
            MinimumSize = minimumSize,
            FormBorderStyle = FormBorderStyle.Sizable,
            BackColor = _palette.Page,
            ForeColor = _palette.Text,
            Font = Font
        };
        NativeTheme.ApplyWindowTheme(dialog.Handle, _palette);
        return dialog;
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            return null;
        }
    }

    private FlowLayoutPanel ScreenLayout(Form dialog)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            RowCount = 2,
            ColumnCount = 1,
            BackColor = _palette.Page
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        dialog.Controls.Add(root);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = _palette.Page,
            Margin = new Padding(0, 0, 0, 12)
        };
        flow.HandleCreated += (_, _) => NativeTheme.ApplyControlTheme(flow.Handle, _palette.IsDark);
        root.Controls.Add(flow, 0, 0);
        return flow;
    }

    private FlowLayoutPanel AddScreenFooter(Form dialog, string closeText)
    {
        var root = dialog.Controls.OfType<TableLayoutPanel>().First();
        var buttons = FooterButtons(dialog, closeText);
        root.Controls.Add(buttons, 0, 1);
        return buttons;
    }

    private FlowLayoutPanel FooterButtons(Form dialog, string closeText)
    {
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = _palette.Page,
            Margin = new Padding(0)
        };
        var close = DialogButton(closeText, _palette.Button, _palette.Text);
        close.Click += (_, _) => dialog.Close();
        buttons.Controls.Add(close);
        return buttons;
    }

    private APanel InstructionSection(string title, string text, int height)
    {
        var panel = new APanel
        {
            Width = 680,
            Height = height,
            Radius = 14,
            BorderWidth = 1,
            Padding = new Padding(18, 14, 18, 14),
            Margin = new Padding(0, 0, 10, 14),
            Shadow = 0,
            ShadowOpacity = 0F
        };
        StylePanel(panel);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(layout);
        layout.Controls.Add(new ALabel
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Variable Text", 11F, FontStyle.Bold),
            ForeColor = _palette.Text
        }, 0, 0);
        layout.Controls.Add(new ALabel
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextMultiLine = true,
            ForeColor = _palette.Text
        }, 0, 1);
        return panel;
    }

    private TableLayoutPanel UsageHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = _palette.Page,
            Margin = new Padding(0, 0, 0, 6)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        header.Controls.Add(HeaderLabel(T("limits.profile")), 0, 0);
        header.Controls.Add(HeaderLabel(T("limits.fiveHour")), 1, 0);
        header.Controls.Add(HeaderLabel(T("limits.week")), 2, 0);
        header.Controls.Add(HeaderLabel(T("limits.state")), 3, 0);
        return header;
    }

    private ALabel HeaderLabel(string text)
    {
        return new ALabel
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = _palette.Muted,
            Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold)
        };
    }

    private UsageRowControls CreateUsageRow(AccountProfile profile, string? activeProfile)
    {
        var isActive = string.Equals(profile.Name, activeProfile, StringComparison.OrdinalIgnoreCase);
        var authPath = isActive && File.Exists(_layout.AuthJsonPath)
            ? _layout.AuthJsonPath
            : _layout.ProfileAuthPath(profile.Name);

        var panel = new APanel
        {
            Width = 760,
            Height = 88,
            Radius = 14,
            BorderWidth = isActive ? 2 : 1,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0, 0, 0, 10),
            Shadow = 0,
            ShadowOpacity = 0F
        };
        StyleCard(panel, isActive);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        panel.Controls.Add(layout);

        layout.Controls.Add(new ALabel
        {
            Text = isActive ? profile.DisplayName + " • " + T("status.activeSuffix") : profile.DisplayName,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI Variable Text", 10.5F, FontStyle.Bold)
        }, 0, 0);

        var fiveHour = UsageValueLabel();
        var weekly = UsageValueLabel();
        var state = new ALabel
        {
            Text = File.Exists(authPath) ? T("limits.ready") : T("limits.notSaved"),
            Dock = DockStyle.Fill,
            TextMultiLine = true,
            ForeColor = _palette.Muted
        };
        layout.Controls.Add(fiveHour, 1, 0);
        layout.Controls.Add(weekly, 2, 0);
        layout.Controls.Add(state, 3, 0);

        return new UsageRowControls(panel, authPath, fiveHour, weekly, state);
    }

    private ALabel UsageValueLabel()
    {
        return new ALabel
        {
            Text = "--" + Environment.NewLine + T("limits.unknownReset"),
            Dock = DockStyle.Fill,
            TextMultiLine = true,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold)
        };
    }

    private void SetUsageRowStatus(UsageRowControls row, string status)
    {
        row.State.Text = status;
        row.FiveHour.Text = "--" + Environment.NewLine + T("limits.unknownReset");
        row.Weekly.Text = "--" + Environment.NewLine + T("limits.unknownReset");
    }

    private void SetUsageRowResult(UsageRowControls row, CodexUsageWindow? fiveHour, CodexUsageWindow? weekly, string status)
    {
        row.FiveHour.Text = FormatUsageWindow(fiveHour);
        row.Weekly.Text = FormatUsageWindow(weekly);
        row.State.Text = status;
    }

    private string FormatUsageWindow(CodexUsageWindow? window)
    {
        var percent = window?.PercentLeft is null
            ? "--"
            : $"{Math.Round(window.PercentLeft.Value, 1):0.#}%";
        var reset = window?.ResetAt is null
            ? T("limits.unknownReset")
            : F("limits.reset", window.ResetAt.Value.ToLocalTime().ToString("dd.MM HH:mm"));
        return percent + Environment.NewLine + reset;
    }

    private Button DialogButton(string text, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            Width = 142,
            Height = 38,
            Margin = new Padding(10, 0, 0, 0),
            BackColor = backColor,
            ForeColor = foreColor,
            FlatStyle = FlatStyle.Flat
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            UseWaitCursor = true;
            await action();
        }
        catch (Exception ex)
        {
            AppendLog(F("error.prefix", ex.Message));
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void RunSync(Action action)
    {
        try
        {
            UseWaitCursor = true;
            action();
        }
        catch (Exception ex)
        {
            AppendLog(F("error.prefix", ex.Message));
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void RefreshStatus()
    {
        var active = _switcher.ReadActiveProfile();
        _activeLabel.Text = active is null
            ? T("status.unknown")
            : F("status.active", _switcher.DisplayName(active));

        _cards.SuspendLayout();
        _cards.Controls.Clear();
        foreach (var profile in _switcher.ListProfiles())
        {
            _cards.Controls.Add(AccountCard(profile, active));
        }
        _cards.ResumeLayout();
        RefreshProfileSettings();
        ApplyResponsiveLayout();
    }

    private void RefreshProfileSettings()
    {
        if (_profileSettingsList.IsDisposed)
        {
            return;
        }

        var active = _switcher.ReadActiveProfile();
        _profileSettingsList.SuspendLayout();
        _profileSettingsList.Controls.Clear();
        foreach (var profile in _switcher.ListProfiles())
        {
            var row = new TableLayoutPanel
            {
                Width = SettingsProfileRowWidth(),
                Height = 50,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 8)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            row.Controls.Add(new ALabel
            {
                Text = string.Equals(profile.Name, active, StringComparison.OrdinalIgnoreCase)
                    ? profile.DisplayName + "  • " + T("status.activeSuffix")
                    : profile.DisplayName,
                Dock = DockStyle.Fill,
                AutoEllipsis = true
            }, 0, 0);
            var delete = new AButton
            {
                Text = T("settings.delete"),
                Dock = DockStyle.Fill,
                Radius = 10,
                Enabled = !string.Equals(profile.Name, active, StringComparison.OrdinalIgnoreCase),
                Type = AntdUI.TTypeMini.Default
            };
            delete.Click += (_, _) => DeleteProfileFromSettings(profile);
            row.Controls.Add(delete, 1, 0);
            _profileSettingsList.Controls.Add(row);
        }
        _profileSettingsList.ResumeLayout();
        ResizeProfileSettingsRows(SettingsProfileRowWidth());
        ApplyThemeToProfileRows();
    }

    private int SettingsProfileRowWidth()
    {
        if (_profileSettingsList.Parent is Control parent && parent.ClientSize.Width > 40)
        {
            return Math.Max(300, parent.ClientSize.Width - 18);
        }

        return Math.Max(300, _profileSettingsList.ClientSize.Width - 6);
    }

    private void ResizeProfileSettingsRows(int width)
    {
        if (_profileSettingsList.IsDisposed)
        {
            return;
        }

        _profileSettingsList.Width = width;
        foreach (Control row in _profileSettingsList.Controls)
        {
            if (row.Width != width)
            {
                row.Width = width;
            }
        }
    }

    private void AppendLog(string message)
    {
        _log.Text = $"{DateTime.Now:HH:mm}  {message}";
    }

    private static T? FindDescendant<T>(Control root) where T : Control
    {
        foreach (Control control in root.Controls)
        {
            if (control is T match)
            {
                return match;
            }

            var childMatch = FindDescendant<T>(control);
            if (childMatch is not null)
            {
                return childMatch;
            }
        }

        return null;
    }

    private void ResizeCards()
    {
        var width = CurrentCardWidth();
        _cards.SuspendLayout();
        foreach (Control card in _cards.Controls)
        {
            if (card.Width != width)
            {
                card.Width = width;
            }
        }
        _cards.ResumeLayout();
    }

    private void ResizeSidebarActionButtons()
    {
        var width = Math.Max(160, _sidebarActions.ClientSize.Width);
        _sidebarActions.SuspendLayout();
        foreach (Control control in _sidebarActions.Controls)
        {
            if (control.Width != width)
            {
                control.Width = width;
            }
        }
        _sidebarActions.ResumeLayout();
    }

    private void ScheduleResponsiveLayout()
    {
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void ApplyResponsiveLayout()
    {
        _resizeTimer.Stop();
        ResizeCards();
        ResizeSidebarActionButtons();
    }

    private int CurrentCardWidth()
    {
        var available = Math.Max(300, _cards.ClientSize.Width - 22);
        var columns = available >= 900 ? 3 : available >= 700 ? 2 : 1;
        return Math.Max(280, (available - columns * 22) / columns);
    }

    private SwitcherSettings LoadSettings()
    {
        var storedTheme = _database?.GetSetting("theme");
        var storedLanguage = _database?.GetSetting("language");
        var language = Localizer.FromStorage(storedLanguage);
        if (string.Equals(storedTheme, "System", StringComparison.OrdinalIgnoreCase))
        {
            return new SwitcherSettings { Theme = ThemeMode.Auto, Language = language };
        }

        if (Enum.TryParse<ThemeMode>(storedTheme, ignoreCase: true, out var databaseTheme))
        {
            return new SwitcherSettings { Theme = databaseTheme, Language = language };
        }

        try
        {
            if (File.Exists(_settingsPath))
            {
                var loaded = JsonSerializer.Deserialize<SwitcherSettings>(File.ReadAllText(_settingsPath));
                return loaded is null ? new SwitcherSettings() : NormalizeSettings(loaded);
            }
        }
        catch
        {
            // Settings contain no secrets; corrupted settings are safe to reset.
        }

        return new SwitcherSettings { Language = language };
    }

    private static SwitcherSettings NormalizeSettings(SwitcherSettings settings)
    {
        return new SwitcherSettings
        {
            Theme = Enum.IsDefined(settings.Theme) ? settings.Theme : ThemeMode.Auto,
            Language = Enum.IsDefined(settings.Language) ? settings.Language : AppLanguage.Russian
        };
    }

    private void SaveSettings()
    {
        if (_database is not null)
        {
            _database.SetSetting("theme", _settings.Theme.ToString());
            _database.SetSetting("language", Localizer.ToStorage(_settings.Language));
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, JsonOptions));
    }

    private UiPalette ResolvePalette()
    {
        var effective = _settings.Theme == ThemeMode.Auto
            ? IsWindowsDarkMode() ? ThemeMode.Dark : ThemeMode.Light
            : _settings.Theme;
        return effective switch
        {
            ThemeMode.Dark => UiPalette.Dark,
            ThemeMode.Gray => UiPalette.Gray,
            _ => UiPalette.Light
        };
    }

    private void ApplyTheme(bool rebuild)
    {
        _palette = ResolvePalette();
        AntdUI.Config.Mode = _palette.Mode;
        BackColor = _palette.Page;
        ForeColor = _palette.Text;
        _shell.BackColor = _palette.Page;
        _main.BackColor = _palette.Page;
        _cards.BackColor = _palette.Page;
        _sidebarActions.BackColor = _palette.Surface;
        _profileSettingsList.BackColor = _palette.Surface;
        MainMenuStrip!.BackColor = _palette.Menu;
        MainMenuStrip.ForeColor = _palette.Text;
        Text = T("app.title");
        StylePanel(_sidebar);
        StylePanel(_settingsPanel);
        _activeLabel.ForeColor = _palette.Text;
        _themeCaption.Text = F("settings.themeTitle", ThemeTitle(_settings.Theme));
        _themeCaption.ForeColor = _palette.Muted;
        _languageCaption.Text = T("settings.languageTitle");
        _languageCaption.ForeColor = _palette.Muted;
        _newProfileInput.BackColor = _palette.Control;
        _newProfileInput.ForeColor = _palette.Text;
        _log.BackColor = _palette.LogBack;
        _log.ForeColor = _palette.LogText;
        StyleMenu();
        NativeTheme.ApplyWindowTheme(Handle, _palette);
        ApplyThemeToControls(this);
        ApplyThemeToProfileRows();

        if (rebuild)
        {
            RebuildThemeButtons();
            RefreshStatus();
        }
    }

    private void RebuildThemeButtons()
    {
        if (_themeCaption.Parent is not TableLayoutPanel themeBox)
        {
            return;
        }

        for (var i = themeBox.Controls.Count - 1; i >= 1; i--)
        {
            themeBox.Controls.RemoveAt(i);
        }

        themeBox.Controls.Add(ThemeButton(T("theme.auto"), ThemeMode.Auto), 0, 1);
        themeBox.Controls.Add(ThemeButton(T("theme.dark"), ThemeMode.Dark), 0, 2);
        themeBox.Controls.Add(ThemeButton(T("theme.gray"), ThemeMode.Gray), 0, 3);
        themeBox.Controls.Add(ThemeButton(T("theme.light"), ThemeMode.Light), 0, 4);
        ApplyThemeToControls(themeBox);
    }

    private void ApplyThemeToControls(Control root)
    {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case AButton button:
                    StyleButton(button);
                    break;
                case ATag tag:
                    StyleTag(tag);
                    break;
                case APanel panel when panel.Name == "EventSurface":
                    panel.Back = _palette.LogBack;
                    panel.BorderColor = _palette.Line;
                    break;
                case APanel panel:
                    StylePanel(panel);
                    break;
                case ALabel label when ReferenceEquals(label, _log):
                    label.ForeColor = _palette.LogText;
                    label.BackColor = _palette.LogBack;
                    break;
                case ALabel label:
                    label.ForeColor = _palette.Text;
                    break;
                case AInput input when !ReferenceEquals(input, _log):
                    input.BackColor = _palette.Control;
                    input.ForeColor = _palette.Text;
                    break;
                case FlowLayoutPanel flow:
                    flow.BackColor = flow == _cards ? _palette.Page : _palette.Surface;
                    NativeTheme.ApplyControlTheme(flow.Handle, _palette.IsDark);
                    break;
                case TableLayoutPanel table:
                    table.BackColor = Color.Transparent;
                    break;
            }

            ApplyThemeToControls(control);
        }
    }

    private void ApplyThemeToProfileRows()
    {
        foreach (Control row in _profileSettingsList.Controls)
        {
            row.BackColor = _palette.Surface;
        }
    }

    private void StylePanel(APanel panel)
    {
        panel.Back = _palette.Surface;
        panel.BorderColor = _palette.Line;
    }

    private void StyleCard(APanel card, bool active)
    {
        card.Back = _palette.Surface;
        card.BorderColor = active ? _palette.Accent : _palette.Line;
    }

    private void StyleButton(AButton button)
    {
        if (button.Tag is null && button.Type != AntdUI.TTypeMini.Default)
        {
            button.Tag = button.Type.ToString();
        }

        var role = button.Tag as string;
        var selected = role is nameof(AntdUI.TTypeMini.Primary) or nameof(AntdUI.TTypeMini.Success) or nameof(AntdUI.TTypeMini.Info) or nameof(AntdUI.TTypeMini.Warn);
        var accent = role is nameof(AntdUI.TTypeMini.Info) or nameof(AntdUI.TTypeMini.Warn)
            ? _palette.Orange
            : _palette.Accent;
        button.BorderWidth = 0;
        button.Type = AntdUI.TTypeMini.Default;
        button.DefaultBack = selected ? accent : _palette.Button;
        button.BackColor = selected ? accent : _palette.Button;
        button.BackHover = selected ? _palette.AccentHover : _palette.ButtonHover;
        button.BackActive = selected ? _palette.AccentActive : _palette.ButtonActive;
        button.ForeColor = selected ? _palette.ButtonText : _palette.Text;
        button.ForeHover = selected ? _palette.ButtonText : _palette.Text;
        button.ForeActive = selected ? _palette.ButtonText : _palette.Text;
        button.DefaultBorderColor = _palette.Line;
    }

    private void StyleTag(ATag tag)
    {
        var active = string.Equals(tag.Tag as string, "active", StringComparison.OrdinalIgnoreCase);
        tag.Type = AntdUI.TTypeMini.Default;
        tag.BorderWidth = 1;
        tag.BackColor = active ? _palette.AccentActive : _palette.Control;
        tag.ForeColor = active ? _palette.ButtonText : _palette.Muted;
    }

    private void StyleMenu()
    {
        var menu = MainMenuStrip;
        if (menu is null)
        {
            return;
        }

        menu.BackColor = _palette.Menu;
        menu.ForeColor = _palette.Text;
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = _palette.Menu;
            item.ForeColor = _palette.Text;
            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.DropDown.BackColor = _palette.Surface;
                menuItem.DropDown.ForeColor = _palette.Text;
                ConfigureDropDown(menuItem);
                foreach (ToolStripItem dropItem in menuItem.DropDownItems)
                {
                    dropItem.BackColor = _palette.Surface;
                    dropItem.ForeColor = _palette.Text;
                }
            }
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.ApplyWindowTheme(Handle, _palette);
        NativeTheme.ApplyControlTheme(_cards.Handle, _palette.IsDark);
        NativeTheme.ApplyControlTheme(_sidebarActions.Handle, _palette.IsDark);
        NativeTheme.ApplyControlTheme(_profileSettingsList.Handle, _palette.IsDark);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.Theme == ThemeMode.Auto)
        {
            ApplyTheme(rebuild: true);
        }
    }

    private static bool IsWindowsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private sealed record SwitcherSettings
    {
        public ThemeMode Theme { get; init; } = ThemeMode.Auto;
        public AppLanguage Language { get; init; } = AppLanguage.Russian;
    }

    private sealed record UsageRowControls(APanel Panel, string AuthPath, ALabel FiveHour, ALabel Weekly, ALabel State);

    private enum ThemeMode
    {
        Auto,
        Dark,
        Gray,
        Light,
    }

    private string ThemeTitle(ThemeMode theme)
    {
        return theme switch
        {
            ThemeMode.Auto => T("theme.auto.short"),
            ThemeMode.Dark => T("theme.dark.short"),
            ThemeMode.Gray => T("theme.gray.short"),
            ThemeMode.Light => T("theme.light.short"),
            _ => T("theme.auto.short")
        };
    }

    private string T(string key)
    {
        return Localizer.Get(_settings.Language, key);
    }

    private string F(string key, params object?[] args)
    {
        return Localizer.Format(_settings.Language, key, args);
    }

    private sealed record UiPalette(
        Color Page,
        Color Surface,
        Color Control,
        Color Menu,
        Color Text,
        Color Muted,
        Color Line,
        Color Accent,
        Color AccentHover,
        Color AccentActive,
        Color Orange,
        Color Button,
        Color ButtonHover,
        Color ButtonActive,
        Color ButtonText,
        Color LogBack,
        Color LogText,
        AntdUI.TMode Mode,
        bool IsDark)
    {
        public static UiPalette Light { get; } = new(
            Color.FromArgb(244, 238, 228),
            Color.FromArgb(255, 252, 246),
            Color.FromArgb(239, 231, 219),
            Color.FromArgb(232, 222, 208),
            Color.FromArgb(38, 37, 31),
            Color.FromArgb(111, 103, 91),
            Color.FromArgb(216, 203, 187),
            Color.FromArgb(103, 132, 96),
            Color.FromArgb(119, 147, 111),
            Color.FromArgb(86, 112, 81),
            Color.FromArgb(178, 111, 64),
            Color.FromArgb(238, 230, 218),
            Color.FromArgb(231, 219, 202),
            Color.FromArgb(219, 205, 186),
            Color.FromArgb(255, 252, 246),
            Color.FromArgb(239, 231, 219),
            Color.FromArgb(38, 37, 31),
            AntdUI.TMode.Light,
            false);

        public static UiPalette Dark { get; } = new(
            Color.FromArgb(5, 6, 5),
            Color.FromArgb(12, 13, 11),
            Color.FromArgb(20, 21, 18),
            Color.FromArgb(5, 6, 5),
            Color.FromArgb(241, 237, 229),
            Color.FromArgb(171, 163, 148),
            Color.FromArgb(48, 45, 38),
            Color.FromArgb(126, 154, 111),
            Color.FromArgb(142, 169, 126),
            Color.FromArgb(104, 130, 91),
            Color.FromArgb(190, 118, 68),
            Color.FromArgb(16, 17, 15),
            Color.FromArgb(36, 34, 29),
            Color.FromArgb(48, 45, 38),
            Color.FromArgb(5, 6, 5),
            Color.FromArgb(20, 21, 18),
            Color.FromArgb(241, 237, 229),
            AntdUI.TMode.Dark,
            true);

        public static UiPalette Gray { get; } = new(
            Color.FromArgb(34, 34, 29),
            Color.FromArgb(43, 42, 35),
            Color.FromArgb(56, 53, 44),
            Color.FromArgb(29, 30, 26),
            Color.FromArgb(238, 232, 221),
            Color.FromArgb(183, 174, 160),
            Color.FromArgb(89, 82, 68),
            Color.FromArgb(135, 159, 120),
            Color.FromArgb(148, 172, 133),
            Color.FromArgb(115, 139, 101),
            Color.FromArgb(196, 126, 75),
            Color.FromArgb(25, 26, 23),
            Color.FromArgb(62, 58, 49),
            Color.FromArgb(75, 69, 58),
            Color.FromArgb(25, 26, 23),
            Color.FromArgb(21, 22, 19),
            Color.FromArgb(238, 232, 221),
            AntdUI.TMode.Dark,
            true);
    }

    private static class NativeTheme
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaBorderColor = 34;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;
        private const int DwmWindowCornerPreferenceRound = 2;

        public static void ApplyWindowTheme(IntPtr handle, UiPalette palette)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var dark = palette.IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            var corner = DwmWindowCornerPreferenceRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corner, sizeof(int));
            var caption = ToColorRef(palette.Menu);
            var text = ToColorRef(palette.Text);
            var border = ToColorRef(palette.Line);
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref text, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));
        }

        public static void ApplyControlTheme(IntPtr handle, bool dark)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            SetWindowTheme(handle, dark ? "DarkMode_Explorer" : "Explorer", null);
        }

        public static void SetRedraw(IntPtr handle, bool enabled)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            SendMessage(handle, 0x000B, enabled ? 1 : 0, 0);
        }

        private static int ToColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    }

    private sealed class AppMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Func<UiPalette> _palette;

        public AppMenuRenderer(Func<UiPalette> palette)
            : base(new AppMenuColorTable(palette))
        {
            _palette = palette;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(_palette().Line);
            var bounds = new Rectangle(Point.Empty, e.ToolStrip.Size - new Size(1, 1));
            e.Graphics.DrawRectangle(pen, bounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var palette = _palette();
            var selected = e.Item.Selected || e.Item.Pressed;
            using var brush = new SolidBrush(selected ? palette.ButtonHover : palette.Menu);
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            e.Graphics.FillRectangle(brush, bounds);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var palette = _palette();
            using var pen = new Pen(palette.Line);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = _palette().Muted;
            base.OnRenderArrow(e);
        }
    }

    private sealed class AppMenuColorTable : ProfessionalColorTable
    {
        private readonly Func<UiPalette> _palette;

        public AppMenuColorTable(Func<UiPalette> palette)
        {
            _palette = palette;
            UseSystemColors = false;
        }

        public override Color MenuItemSelected => _palette().ButtonHover;
        public override Color MenuItemSelectedGradientBegin => _palette().ButtonHover;
        public override Color MenuItemSelectedGradientEnd => _palette().ButtonHover;
        public override Color MenuItemPressedGradientBegin => _palette().ButtonActive;
        public override Color MenuItemPressedGradientEnd => _palette().ButtonActive;
        public override Color ToolStripDropDownBackground => _palette().Surface;
        public override Color ImageMarginGradientBegin => _palette().Surface;
        public override Color ImageMarginGradientMiddle => _palette().Surface;
        public override Color ImageMarginGradientEnd => _palette().Surface;
        public override Color MenuBorder => _palette().Line;
        public override Color MenuItemBorder => _palette().Line;
        public override Color SeparatorDark => _palette().Line;
        public override Color SeparatorLight => _palette().Line;
    }
}
