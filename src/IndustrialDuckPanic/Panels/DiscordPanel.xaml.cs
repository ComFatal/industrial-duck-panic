using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialDuckPanic.Core.Discord;
using IndustrialDuckPanic.ViewModels;
using Microsoft.Win32;

namespace IndustrialDuckPanic.Panels;

public partial class DiscordPanel : UserControl
{
    private readonly MainViewModel _vm;
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

    public DiscordPanel(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        ResetForm();
    }

    /// <summary>Fills the form with the active config's texts (or the defaults). The webhook URL is never shown.</summary>
    private void ResetForm()
    {
        var messages = _vm.Config?.Messages ?? new MessageSet();
        MsgTest.Text = messages.Test;
        MsgTackled.Text = messages.Tackled;
        MsgPanic.Text = messages.Panic;
        MsgDestroyed.Text = messages.Destroyed;
        MsgOk.Text = messages.ImOk;
        WebhookUrlBox.Password = "";

        var ping = _vm.Config?.Ping ?? new PingSettings();
        PingModeBox.SelectedIndex = (int)ping.Mode;
        RoleId.Text = ping.RoleId ?? "";

        Owner.SelectedItem = _vm.OwnerChoices.FirstOrDefault(o => _vm.Config is { } c && o.Type == c.OwnerType && o.Id == c.OwnerId)
                             ?? _vm.OwnerChoices.LastOrDefault();
        CreateResult.Visibility = Visibility.Collapsed;
    }

    private void PingMode_Changed(object sender, SelectionChangedEventArgs e) =>
        RoleRow.Visibility = PingModeBox.SelectedIndex == (int)Core.Discord.PingMode.Role ? Visibility.Visible : Visibility.Collapsed;

    private void Cancel_Click(object sender, RoutedEventArgs e) => ResetForm();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!WebhookUrl_IsValid()) return;
        if (Owner.SelectedItem is not OwnerChoice owner)
        {
            Show(CreateResult, false, "Your corporation is not known yet. Wait a few seconds and try again.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = "IDP.config",
            Filter = "Industrial Duck Panic configuration (*.config)|*.config",
            DefaultExt = ".config",
            Title = "Save the configuration to share",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var messages = new MessageSet
        {
            Test = MsgTest.Text.Trim(),
            Tackled = MsgTackled.Text.Trim(),
            Panic = MsgPanic.Text.Trim(),
            Destroyed = MsgDestroyed.Text.Trim(),
            ImOk = MsgOk.Text.Trim(),
        };
        var ping = new PingSettings { Mode = (Core.Discord.PingMode)PingModeBox.SelectedIndex, RoleId = RoleId.Text.Trim() };
        var (ok, message) = _vm.CreateConfigFile(dialog.FileName, WebhookUrlBox.Password, messages, ping, owner);
        Show(CreateResult, ok, message);
        if (ok) WebhookUrlBox.Password = "";
    }

    private bool WebhookUrl_IsValid()
    {
        if (Core.Discord.WebhookUrl.IsValid(WebhookUrlBox.Password)) return true;
        Show(CreateResult, false, "Paste a Discord webhook URL. It starts with https://discord.com/api/webhooks/");
        return false;
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            FileName = "IDP.config",
            Filter = "Industrial Duck Panic configuration (*.config;*.conf)|*.config;*.conf|All files (*.*)|*.*",
            Title = "Load the IDP.config file",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var (ok, message) = _vm.LoadConfigFile(dialog.FileName);
        Show(LoadResult, ok, message);
        if (ok) ResetForm();
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        try { await _vm.SendTestAsync(); }
        finally { button.IsEnabled = true; }
    }

    private static void Show(TextBlock target, bool ok, string message)
    {
        target.Text = message;
        target.Foreground = ok ? OkBrush : ErrorBrush;
        target.Visibility = Visibility.Visible;
    }
}
