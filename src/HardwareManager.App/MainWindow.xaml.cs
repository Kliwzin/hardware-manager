using HardwareManager.App.Pages;
using HardwareManager.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HardwareManager.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainWindowViewModel viewModel, DetailViewModel detail, IServiceProvider services)
    {
        DataContext = viewModel;
        InitializeComponent();

        SystemThemeWatcher.Watch(this);              // segue o tema claro/escuro do Windows
        RootNavigation.SetServiceProvider(services); // as páginas vêm do contêiner de DI

        // A tela de detalhe não está no menu: abre ao clicar num card e volta pelo botão "Voltar".
        detail.ShowRequested += () => RootNavigation.Navigate(typeof(DetailPage));
        detail.BackRequested += () => RootNavigation.Navigate(typeof(OverviewPage));

        Loaded += async (_, _) =>
        {
            RootNavigation.Navigate(typeof(OverviewPage));
            await viewModel.InitializeAsync();
        };
    }
}