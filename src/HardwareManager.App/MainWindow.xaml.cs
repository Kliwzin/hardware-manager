using HardwareManager.App.Pages;
using HardwareManager.App.ViewModels;
using HidSharp.Reports;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HardwareManager.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainWindowViewModel viewModel, IServiceProvider services)
    {
        DataContext = viewModel;
        InitializeComponent();

        SystemThemeWatcher.Watch(this);              // segue o tema claro/escuro do Windows
        RootNavigation.SetServiceProvider(services); // as páginas vêm do contêiner de DI

        Loaded += async (_, _) =>
        {
            RootNavigation.Navigate(typeof(OverviewPage));
            await viewModel.InitializeAsync();
        };
    }
}