using System.Windows;
using System.Windows.Threading;
using CoreWindows.Sensors;

namespace HardwareManager.App.Services;

/// <summary>Repassa as leituras do SensorService (outra thread) para a thread da interface.</summary>
public sealed class UiSensorFeed
{
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;

    public UiSensorFeed(SensorService sensors)
    {
        Sensors = sensors;
        sensors.SnapshotTaken += (_, snapshot) =>
            _dispatcher.InvokeAsync(() => Updated?.Invoke(snapshot));
    }

    public SensorService Sensors { get; }

    /// <summary>Disparado na thread da interface, uma vez por leitura.</summary>
    public event Action<SensorSnapshot>? Updated;
}