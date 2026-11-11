using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CoreWindows.Sensors;
using LiveChartsCore.Defaults;
using SkiaSharp;

namespace HardwareManager.App.ViewModels;

/// <summary>
/// Um sensor dentro de um gráfico: os pontos da linha + a linha da tabela (atual, mínimo, média, máximo).
/// </summary>
public sealed partial class ChartRowViewModel : ObservableObject
{
    private readonly int _capacity;
    private DateTimeOffset _last = DateTimeOffset.MinValue; // horário do último ponto já colocado no gráfico

    public ChartRowViewModel(SensorInfo sensor, string label, SKColor color, SensorHistory history, SensorSnapshot? latest)
    {
        Sensor = sensor;
        Label = label;
        Color = color;

        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue));
        brush.Freeze();
        Swatch = brush;

        // Começa com tudo o que o histórico já tem (até 30 min), não só com o que chegar daqui para a frente.
        _capacity = history.Capacity;
        var points = history.GetSeries(sensor.Id);
        Values = new ObservableCollection<DateTimePoint>(points.Select(ToChartPoint));
        if (points.Count > 0)
            _last = points[^1].Timestamp;

        if (latest is not null)
            ShowCurrent(latest.Values.TryGetValue(sensor.Id, out double value) ? value : null);
        ShowStats(history.GetStats(sensor.Id));
    }

    public SensorInfo Sensor { get; }
    public string Label { get; }

    /// <summary>Cor da linha no gráfico (SkiaSharp) e a mesma cor para o quadradinho da tabela (WPF).</summary>
    public SKColor Color { get; }
    public Brush Swatch { get; }

    /// <summary>Pontos da linha. O gráfico acompanha as alterações desta coleção sozinho.</summary>
    public ObservableCollection<DateTimePoint> Values { get; }

    [ObservableProperty] private string _current = "—";
    [ObservableProperty] private string _min = "—";
    [ObservableProperty] private string _average = "—";
    [ObservableProperty] private string _max = "—";
    [ObservableProperty] private bool _isSuspect;

    public void Update(SensorSnapshot snapshot, SensorHistory history)
    {
        // A leitura pode já ter vindo do histórico: as notificações chegam à interface com um pequeno
        // atraso, então uma leitura feita logo antes de a tela abrir aparece nos dois lugares.
        if (snapshot.Timestamp <= _last)
            return;
        _last = snapshot.Timestamp;

        double? value = snapshot.Values.TryGetValue(Sensor.Id, out double v) ? v : null;

        Values.Add(new DateTimePoint(snapshot.Timestamp.LocalDateTime, value));
        while (Values.Count > _capacity)
            Values.RemoveAt(0);

        ShowCurrent(value);
        ShowStats(history.GetStats(Sensor.Id));
    }

    private static DateTimePoint ToChartPoint(HistoryPoint point)
        => new(point.Timestamp.LocalDateTime, point.Value); // valor nulo = buraco na linha

    private void ShowCurrent(double? value)
    {
        Current = value is { } v ? SensorFormat.Format(Sensor.Kind, v) : "—";
        IsSuspect = value is { } x && !SensorPlausibility.IsPlausible(Sensor.Kind, x);
    }

    private void ShowStats(SensorStats? stats)
    {
        Min = stats is { } s ? SensorFormat.Format(Sensor.Kind, s.Min) : "—";
        Average = stats is { } a ? SensorFormat.Format(Sensor.Kind, a.Average) : "—";
        Max = stats is { } m ? SensorFormat.Format(Sensor.Kind, m.Max) : "—";
    }
}
