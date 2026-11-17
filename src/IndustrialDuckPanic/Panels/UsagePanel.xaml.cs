using System.Windows.Controls;
using IndustrialDuckPanic.ViewModels;

namespace IndustrialDuckPanic.Panels;

public partial class UsagePanel : UserControl
{
    public UsagePanel(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
