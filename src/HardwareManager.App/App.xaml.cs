using System.Windows;
using CoreWindows.Sensors;
using CoreWindows.Storage;
using HardwareManager.App.Pages;
using HardwareManager.App.Services;
using HardwareManager.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HardwareManager.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // Núcleo
        services.AddSingleton(_ => new SensorService(LhmSensorSource.CreateDefault));
        services.AddSingleton(_ => new HistoryStore(HistoryStore.PathFor("HardwareManager")));
        services.AddSingleton<HistoryRecorder>();
        services.AddSingleton<UiSensorFeed>();
        services.AddSingleton<SensorPreferences>();

        // Janela e páginas (cada uma com seu ViewModel)
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<OverviewPage>();
        services.AddSingleton<SensorsViewModel>();
        services.AddSingleton<SensorsPage>();
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<SystemPage>();
        services.AddSingleton<DetailViewModel>();
        services.AddSingleton<DetailPage>();

        _services = services.BuildServiceProvider();

        // O gravador assina as leituras antes de o serviço começar, para não perder a primeira.
        _services.GetRequiredService<HistoryRecorder>().Start();
        _services.GetRequiredService<SensorService>().Start();
        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Descarta tudo o que o contêiner criou, na ordem inversa da criação: primeiro o HistoryRecorder
        // (grava o minuto em andamento), depois o SensorService (para o laço e fecha o LHM).
        // Bloquear aqui é seguro porque o CoreWindows usa ConfigureAwait(false).
        _services?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}