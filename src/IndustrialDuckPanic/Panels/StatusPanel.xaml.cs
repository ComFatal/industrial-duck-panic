using System.Windows;
using System.Windows.Controls;
using IndustrialDuckPanic.Core.Logs;
using IndustrialDuckPanic.Services;
using IndustrialDuckPanic.ViewModels;
using Microsoft.Win32;

namespace IndustrialDuckPanic.Panels;

public partial class StatusPanel : UserControl
{
    private readonly MainViewModel _vm;

    public StatusPanel(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) =>
        {
            Topmost.IsChecked = Window.GetWindow(this)?.Topmost == true;
            ShowFolder();
            UpdateText.Text = $"Version {UpdateService.CurrentVersion}";
        };
    }

    private void ShowFolder() =>
        FolderText.Text = _vm.Settings.GamelogFolder ?? GameLogWatcher.DefaultFolder;

    private void Topmost_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } w) w.Topmost = Topmost.IsChecked == true;
        _vm.Settings.Topmost = Topmost.IsChecked == true;
        _vm.SaveSettings();
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "EVE Gamelogs folder", InitialDirectory = FolderText.Text };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _vm.SetGamelogFolder(dialog.FolderName);
        ShowFolder();
    }

    private void FolderDefault_Click(object sender, RoutedEventArgs e)
    {
        _vm.SetGamelogFolder(null);
        ShowFolder();
    }

    private void Simulate_Click(object sender, RoutedEventArgs e) => _vm.SimulateTackle();

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        try { UpdateText.Text = await UpdateService.CheckAndApplyAsync(status => UpdateText.Text = status); }
        finally { button.IsEnabled = true; }
    }
}
