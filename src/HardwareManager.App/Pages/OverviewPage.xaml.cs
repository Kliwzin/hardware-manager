using System.Windows.Controls;
using HardwareManager.App.ViewModels;

namespace HardwareManager.App.Pages;

public partial class OverviewPage : Page
{
    public OverviewPage(OverviewViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}