using System.Windows;
using System.Windows.Controls;
using IndustrialDuckPanic.ViewModels;

namespace IndustrialDuckPanic.Panels;

public partial class CharacterPanel : UserControl
{
    private readonly MainViewModel _vm;

    public CharacterPanel(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
    }

    private async void Login_Click(object sender, RoutedEventArgs e) => await _vm.LoginAsync();

    private void Logout_Click(object sender, RoutedEventArgs e) => _vm.Logout();
}
