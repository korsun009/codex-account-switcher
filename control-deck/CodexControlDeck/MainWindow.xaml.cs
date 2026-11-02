using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CodexControlDeck;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        StartupLog.Write("MainWindow constructor started.");
        InitializeComponent();
        StartupLog.Write("MainWindow XAML initialized.");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        StartupLog.Write("Custom title bar configured.");

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 790));
        StartupLog.Write("Window icon and size configured.");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
        StartupLog.Write("MainPage navigation requested.");
    }
}
