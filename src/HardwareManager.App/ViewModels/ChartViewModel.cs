using CoreWindows.Sensors;
using HardwareManager.App.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

namespace HardwareManager.App.ViewModels;

/// <summary>Um gráfico da tela de detalhe: uma linha por sensor, todos do mesmo tipo (um eixo Y só).</summary>
public sealed class ChartViewModel
{
    public ChartViewModel(ChartGroup group, IReadOnlyList<ChartRowViewModel> rows, ChartColors colors)
    {
        Title = group.Title;
        Rows = rows;
        var kind = group.Kind;

        // Pincel opaco: o gráfico usa a cor do próprio Background para limpar a tela a cada quadro.
        var surface = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(
            colors.Surface.Red, colors.Surface.Green, colors.Surface.Blue));
        surface.Freeze();
        Background = surface;

        Series =
        [
            .. rows.Select(row => new LineSeries<DateTimePoint>
            {
                Name = row.Label,
                Values = row.Values,
                Stroke = new SolidColorPaint(row.Color, 2),
                Fill = null,            // só a linha, sem área pintada embaixo
                GeometrySize = 0,       // sem bolinha em cada ponto (são até 1800 por linha)
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0,     // linha reta entre os pontos: não inventa curva entre duas leituras
                YToolTipLabelFormatter = point => SensorFormat.Format(kind, point.Coordinate.PrimaryValue),
            }),
        ];

        XAxes =
        [
            new DateTimeAxis(TimeSpan.FromSeconds(1), time => time.ToString("HH:mm:ss"))
            {
                TextSize = 12,
                LabelsPaint = new SolidColorPaint(colors.AxisText),
            },
        ];

        var (min, max) = Limits(kind);
        YAxes =
        [
            new Axis
            {
                Labeler = value => SensorFormat.Format(kind, value),
                TextSize = 12,
                LabelsPaint = new SolidColorPaint(colors.AxisText),
                SeparatorsPaint = new SolidColorPaint(colors.GridLine, 1),
                MinLimit = min,
                MaxLimit = max,
                MinStep = MinStepFor(kind),
            },
        ];
    }

    public string Title { get; }
    public IReadOnlyList<ChartRowViewModel> Rows { get; }

    /// <summary>Fundo da área do gráfico, na cor do tema atual.</summary>
    public System.Windows.Media.Brush Background { get; }

    public ISeries[] Series { get; }
    public ICartesianAxis[] XAxes { get; }
    public ICartesianAxis[] YAxes { get; }

    public void Update(SensorSnapshot snapshot, SensorHistory history)
    {
        foreach (var row in Rows)
            row.Update(snapshot, history);
    }

    /// <summary>
    /// Limites fixos do eixo Y. Percentuais vão sempre de 0 a 100; temperatura e tensão se ajustam aos dados
    /// (o que interessa é a variação); o resto começa em zero para a altura da linha ser proporcional ao valor.
    /// </summary>
    private static (double? Min, double? Max) Limits(SensorKind kind) => kind switch
    {
        SensorKind.Load or SensorKind.Control or SensorKind.Level => (0, 100),
        SensorKind.Temperature or SensorKind.Voltage => (null, null),
        _ => (0, null)
    };

    /// <summary>
    /// Menor distância entre duas marcas do eixo Y. Tipos exibidos sem casas decimais (RPM, MHz, MB)
    /// usam 1: com passo menor, o eixo repetia o mesmo rótulo ("0 RPM, 0 RPM, 0 RPM, 1 RPM").
    /// Zero = o gráfico escolhe sozinho.
    /// </summary>
    private static double MinStepFor(SensorKind kind) => kind switch
    {
        SensorKind.Fan or SensorKind.Clock or SensorKind.Factor or SensorKind.SmallData => 1,
        _ => 0
    };
}