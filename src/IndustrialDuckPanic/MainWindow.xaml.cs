using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using IndustrialDuckPanic.Panels;
using IndustrialDuckPanic.ViewModels;

namespace IndustrialDuckPanic;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly ToggleButton[] _tabs;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _tabs = [TabCharacter, TabDiscord, TabUsage, TabLegal, TabStatus];

        var args = Environment.GetCommandLineArgs();
        var demoIndex = Array.IndexOf(args, "--demo");
        var demo = demoIndex < 0 ? null : demoIndex + 1 < args.Length ? args[demoIndex + 1] : "rorqual";

        if (demo is null) RestoreWindow();
        Loaded += async (_, _) =>
        {
            if (demo is not null)
            {
                _vm.StartDemo(demo);
                if (demo == "panic") Panic.PlayDemo();
            }
            else
            {
                await _vm.StartAsync();
            }
            if (Array.IndexOf(args, "--panel") is var p and >= 0 && p + 1 < args.Length) OpenPanel(args[p + 1]);
        };
        Closing += (_, _) => { if (demo is null) { SaveWindow(); _vm.Shutdown(); } };
        SourceInitialized += (_, _) => UseDarkTitleBar();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) ClosePanel(); };

        // Leaving the character: close panels that need one.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsConnected) && !_vm.IsConnected && _openTab is "discord" or "usage" or "status")
                ClosePanel();
        };
    }

    // ------------------------------------------------------------ panels

    private string? _openTab;

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        var tab = (ToggleButton)sender;
        var key = (string)tab.Tag;
        if (_openTab == key) { ClosePanel(); return; }
        OpenPanel(key);
    }

    public void OpenPanel(string key)
    {
        _openTab = key;
        foreach (var t in _tabs) t.IsChecked = (string)t.Tag == key;
        (PanelTitle.Text, PanelContent.Content) = key switch
        {
            "character" => ("Character", (object)new CharacterPanel(_vm)),
            "discord" => ("Discord", new DiscordPanel(_vm)),
            "usage" => ("Usage", new UsagePanel(_vm)),
            "legal" => ("Legal", new LegalPanel()),
            _ => ("Status", new StatusPanel(_vm)),
        };
        PanelHost.Visibility = Visibility.Visible;
    }

    private void ClosePanel()
    {
        _openTab = null;
        foreach (var t in _tabs) t.IsChecked = false;
        PanelHost.Visibility = Visibility.Collapsed;
        PanelContent.Content = null;
    }

    private void ClosePanel_Click(object sender, RoutedEventArgs e) => ClosePanel();

    // ------------------------------------------------------------ actions

    private async void Login_Click(object sender, RoutedEventArgs e) => await _vm.LoginAsync();
    private void CancelLogin_Click(object sender, RoutedEventArgs e) => _vm.CancelLogin();
    private async void Panic_Pressed(object? sender, EventArgs e) => await _vm.PanicAsync();
    private async void ImOk_Click(object sender, RoutedEventArgs e) => await _vm.ImOkAsync();
    private void Silence_Click(object sender, RoutedEventArgs e) => _vm.SilenceTackle();

    // ------------------------------------------------------------ window state

    private void RestoreWindow()
    {
        var s = _vm.Settings;
        if (s.WindowWidth is > 300 and < 4000) Width = s.WindowWidth.Value;
        if (s.WindowHeight is > 400 and < 4000) Height = s.WindowHeight.Value;
        if (s.WindowLeft is { } l && s.WindowTop is { } t &&
            l >= SystemParameters.VirtualScreenLeft && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            t >= SystemParameters.VirtualScreenTop && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        Topmost = s.Topmost;
    }

    private void SaveWindow()
    {
        var s = _vm.Settings;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.WindowLeft = b.Left;
        s.WindowTop = b.Top;
        s.WindowWidth = b.Width;
        s.WindowHeight = b.Height;
        s.Topmost = Topmost;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar with the panel colour on Windows 10/11.</summary>
    private void UseDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));          // DWMWA_USE_IMMERSIVE_DARK_MODE
        var caption = 0x0016191B;                                       // COLORREF 0x00BBGGRR = #1B1916
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));     // DWMWA_CAPTION_COLOR (Win11)
    }
}
