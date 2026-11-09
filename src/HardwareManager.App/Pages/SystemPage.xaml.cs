using System.Windows.Controls;
using HardwareManager.App.ViewModels;

namespace HardwareManager.App.Pages;

public partial class SystemPage : Page
{
    public SystemPage(SystemViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}