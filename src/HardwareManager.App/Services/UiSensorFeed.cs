using System.Windows;
using System.Windows.Threading;
using CoreWindows.Sensors;
using CoreWindows.Storage;

namespace HardwareManager.App.Services;

/// <summary>
/// Repassa para a thread da interface o que acontece em outras threads:
/// as leituras do SensorService e as falhas dele e do HistoryRecorder.
/// </summary>
public sealed class UiSensorFeed
{
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;

    public UiSensorFeed(SensorService sensors, HistoryRecorder recorder)
    {
        Sensors = sensors;

        sensors.SnapshotTaken += (_, snapshot) =>
            _dispatcher.InvokeAsync(() => Updated?.Invoke(snapshot));

        sensors.ReadFailed += (_, ex) =>
            Report("Falha ao ler os sensores", $"{ex.Message} — o app continua tentando a cada segundo.");

        recorder.Failed += ex =>
            Report("Histórico não está sendo gravado", $"{ex.Message} — os sensores continuam funcionando normalmente.");
    }

    public SensorService Sensors { get; }

    /// <summary>Disparado na thread da interface, uma vez por leitura.</summary>
    public event Action<SensorSnapshot>? Updated;

    /// <summary>Disparado na thread da interface quando algo falha em segundo plano: (título, mensagem).</summary>
    public event Action<string, string>? Failed;

    private void Report(string title, string message)
        => _dispatcher.InvokeAsync(() => Failed?.Invoke(title, message));
}