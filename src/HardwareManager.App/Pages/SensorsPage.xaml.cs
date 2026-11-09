using System.Windows.Controls;
using HardwareManager.App.ViewModels;

namespace HardwareManager.App.Pages;

public partial class SensorsPage : Page
{
    public SensorsPage(SensorsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}