using System.Windows.Controls;
using HardwareManager.App.ViewModels;

namespace HardwareManager.App.Pages;

public partial class DetailPage : Page
{
    public DetailPage(DetailViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
