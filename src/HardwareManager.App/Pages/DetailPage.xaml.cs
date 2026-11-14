using System.Windows.Controls;
using HardwareManager.App.ViewModels;

namespace HardwareManager.App.Pages;

public partial class DetailPage : Page
{
    public DetailPage(DetailViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        // O ViewModel só relê o histórico longo do banco enquanto a página está na tela.
        Loaded += (_, _) => viewModel.IsActive = true;
        Unloaded += (_, _) => viewModel.IsActive = false;
    }
}