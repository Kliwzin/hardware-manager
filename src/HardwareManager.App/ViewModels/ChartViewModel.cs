using CoreWindows.Sensors;
using CoreWindows.Storage;
using HardwareManager.App.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

namespace HardwareManager.App.ViewModels;

/// <summary>
/// Um gráfico da tela de detalhe: uma linha por sensor, todos do mesmo tipo (um eixo Y só).
/// Sem período (<c>range</c> nulo) é o gráfico ao vivo dos últimos 30 min; com período, é o histórico longo.
/// </summary>
public sealed class ChartViewModel
{
    private readonly DateTimeAxis _timeAxis;

    public ChartViewModel(ChartGroup group, IReadOnlyList<ChartRowViewModel> rows, ChartColors colors, HistoryRange? range)
    {
        Title = group.Title;
        Rows = rows;
        var kind = group.Kind;
        bool live = range is null;

        Caption = range is null
            ? "Gráfico: últimos 30 minutos. Mínimo, média e máximo: desde que o app abriu."
            : $"Gráfico: {range.Description} (média de cada intervalo). Mínimo, média e máximo: do período mostrado.";

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
                // Ao vivo: sem bolinha em cada ponto (são até 1800 por linha, e a linha é contínua).
                // Período: uma bolinha pequena, da cor da linha. Um ponto sozinho entre dois buracos
                // (o PC ficou ligado só naquela hora) não tem linha para desenhar; sem a bolinha, sumiria.
                GeometrySize = live ? 0 : 4,
                GeometryFill = live ? null : new SolidColorPaint(row.Color),
                GeometryStroke = null,
                LineSmoothness = 0,     // linha reta entre os pontos: não inventa curva entre duas leituras
                YToolTipLabelFormatter = point => SensorFormat.Format(kind, point.Coordinate.PrimaryValue),
            }),
        ];

        // Ao vivo, o eixo acompanha os dados. No período, os limites são fixados em ShowPeriod: o eixo
        // mostra o período inteiro mesmo que só exista histórico de uma parte dele.
        string timeFormat = TimeFormatFor(range);
        _timeAxis = new DateTimeAxis(range?.Bucket ?? TimeSpan.FromSeconds(1), time => time.ToString(timeFormat))
        {
            TextSize = 12,
            LabelsPaint = new SolidColorPaint(colors.AxisText),
        };
        XAxes = [_timeAxis];

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

    /// <summary>Texto embaixo da tabela: o que o gráfico e as colunas mostram neste modo.</summary>
    public string Caption { get; }

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
    /// Modo período: põe em cada linha os pontos lidos do banco e fixa o eixo do tempo em [from, to).
    /// Sensor que não está em <paramref name="data"/> fica com a linha vazia.
    /// </summary>
    public void ShowPeriod(IReadOnlyDictionary<string, HistorySeries> data, DateTimeOffset from, DateTimeOffset to)
    {
        // Os pontos usam horário local (como no gráfico ao vivo), então os limites também.
        _timeAxis.MinLimit = from.LocalDateTime.Ticks;
        _timeAxis.MaxLimit = to.LocalDateTime.Ticks;

        foreach (var row in Rows)
            row.ShowPeriod(data.TryGetValue(row.Sensor.Id, out var series) ? series : HistorySeries.Empty);
    }

    /// <summary>Formato dos horários no eixo X: quanto maior o período, menos detalhe.</summary>
    private static string TimeFormatFor(HistoryRange? range) => range switch
    {
        null => "HH:mm:ss",
        { Span.TotalHours: <= 24 } => "HH:mm",
        { Span.TotalDays: <= 31 } => "dd/MM HH'h'",
        _ => "dd/MM/yy",
    };

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