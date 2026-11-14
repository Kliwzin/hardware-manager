using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CoreWindows.Sensors;
using CoreWindows.Storage;
using LiveChartsCore.Defaults;
using SkiaSharp;

namespace HardwareManager.App.ViewModels;

/// <summary>
/// Um sensor dentro de um gráfico: os pontos da linha + a linha da tabela (atual, mínimo, média, máximo).
/// Trabalha em dois modos, escolhidos por quem monta a tela:
/// ao vivo (<see cref="ShowLive"/>: últimos 30 min, um ponto por leitura, estatísticas da sessão) ou
/// período (<see cref="ShowPeriod"/>: histórico longo vindo do banco, estatísticas do período).
/// </summary>
public sealed partial class ChartRowViewModel : ObservableObject
{
    private bool _live;                                     // true = cada leitura nova entra no gráfico
    private int _capacity;                                  // só no modo ao vivo: máximo de pontos na linha
    private DateTimeOffset _last = DateTimeOffset.MinValue; // horário da última leitura já tratada

    public ChartRowViewModel(SensorInfo sensor, string label, SKColor color, SensorSnapshot? latest)
    {
        Sensor = sensor;
        Label = label;
        Color = color;

        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue));
        brush.Freeze();
        Swatch = brush;

        // A coluna "Atual" vale nos dois modos e já nasce preenchida.
        if (latest is not null)
            ShowCurrent(latest.Values.TryGetValue(sensor.Id, out double value) ? value : null);
    }

    public SensorInfo Sensor { get; }
    public string Label { get; }

    /// <summary>Cor da linha no gráfico (SkiaSharp) e a mesma cor para o quadradinho da tabela (WPF).</summary>
    public SKColor Color { get; }
    public Brush Swatch { get; }

    /// <summary>Pontos da linha. O gráfico acompanha as alterações desta coleção sozinho.</summary>
    public ObservableCollection<DateTimePoint> Values { get; } = [];

    [ObservableProperty] private string _current = "—";
    [ObservableProperty] private string _min = "—";
    [ObservableProperty] private string _average = "—";
    [ObservableProperty] private string _max = "—";
    [ObservableProperty] private bool _isSuspect;

    /// <summary>
    /// Modo ao vivo: começa com tudo o que o histórico em memória já tem (até 30 min), não só com o que
    /// chegar daqui para a frente. Mínimo, média e máximo são os da sessão.
    /// </summary>
    public void ShowLive(SensorHistory history)
    {
        _live = true;
        _capacity = history.Capacity;

        var points = history.GetSeries(Sensor.Id);
        Replace(points);
        if (points.Count > 0)
            _last = points[^1].Timestamp;

        ShowStats(history.GetStats(Sensor.Id));
    }

    /// <summary>
    /// Modo período: troca a linha inteira pelos pontos do histórico longo. Mínimo, média e máximo passam
    /// a ser os do período. Pode ser chamado de novo a cada recarga (a linha é substituída, não somada).
    /// </summary>
    public void ShowPeriod(HistorySeries series)
    {
        _live = false;
        Replace(series.Points);
        ShowStats(series.Stats);
    }

    public void Update(SensorSnapshot snapshot, SensorHistory history)
    {
        // A leitura pode já ter vindo do histórico: as notificações chegam à interface com um pequeno
        // atraso, então uma leitura feita logo antes de a tela abrir aparece nos dois lugares.
        if (snapshot.Timestamp <= _last)
            return;
        _last = snapshot.Timestamp;

        double? value = snapshot.Values.TryGetValue(Sensor.Id, out double v) ? v : null;
        ShowCurrent(value);

        // No modo período, a leitura de 1 s só atualiza a coluna "Atual": a linha e as estatísticas
        // vêm do banco e são trocadas a cada recarga.
        if (!_live)
            return;

        Values.Add(new DateTimePoint(snapshot.Timestamp.LocalDateTime, value));
        while (Values.Count > _capacity)
            Values.RemoveAt(0);

        ShowStats(history.GetStats(Sensor.Id));
    }

    private void Replace(IReadOnlyList<HistoryPoint> points)
    {
        Values.Clear();
        foreach (var point in points)
            Values.Add(new DateTimePoint(point.Timestamp.LocalDateTime, point.Value)); // valor nulo = buraco na linha
    }

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