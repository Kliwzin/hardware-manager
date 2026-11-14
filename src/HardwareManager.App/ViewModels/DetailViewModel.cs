using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreWindows.Sensors;
using CoreWindows.Storage;
using HardwareManager.App.Services;

namespace HardwareManager.App.ViewModels;

/// <summary>Uma opção do seletor de período. Range nulo = ao vivo (os 30 min que estão na memória).</summary>
public sealed record PeriodOption(string Label, HistoryRange? Range);

/// <summary>
/// Tela de detalhe de um componente. Dois modos, escolhidos no seletor de período:
/// ao vivo (gráficos dos últimos 30 min + valores da sessão) ou histórico longo (24 h a 1 ano, lido do banco).
/// </summary>
public sealed partial class DetailViewModel : ObservableObject
{
    private readonly UiSensorFeed _feed;
    private readonly SensorPreferences _prefs;
    private readonly HistoryStore _store;
    private ComponentInfo? _component;

    private int _generation;          // muda a cada montagem ou recarga: resposta de consulta antiga é descartada
    private bool _reading;            // há uma consulta ao banco em andamento
    private DateTimeOffset _loadedAt; // horário da última consulta ao banco

    public ObservableCollection<ChartViewModel> Charts { get; } = [];

    public IReadOnlyList<PeriodOption> Periods { get; } =
    [
        new("30 min", null),
        .. HistoryRange.All.Select(range => new PeriodOption(range.Label, range)),
    ];

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _kindLabel = "";

    /// <summary>
    /// Opção marcada no seletor. Nulo = a lista da tela ficou sem seleção (Ctrl+clique no item marcado);
    /// é tratado como "30 min", o modo ao vivo.
    /// </summary>
    [ObservableProperty] private PeriodOption? _selectedPeriod;

    /// <summary>Aviso embaixo do cabeçalho (período sem dados, falha ao ler o banco). Vazio = sem aviso.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = "";

    public bool HasNotice => Notice.Length > 0;

    /// <summary>
    /// A página está na tela? Quem informa é a própria página. Fora da tela, o histórico longo não é
    /// recarregado (não faz sentido consultar o banco para um gráfico que ninguém está vendo).
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>Pede à janela principal para mostrar a página de detalhe.</summary>
    public event Action? ShowRequested;

    /// <summary>Pede à janela principal para voltar à visão geral.</summary>
    public event Action? BackRequested;

    public DetailViewModel(UiSensorFeed feed, SensorPreferences prefs, HistoryStore store)
    {
        _feed = feed;
        _prefs = prefs;
        _store = store;
        SelectedPeriod = Periods[0]; // ainda sem componente: só marca a opção, não monta nada
        _feed.Updated += OnUpdated;
        _prefs.Changed += Build; // renomeou/ocultou na página Sensores → refaz os gráficos
    }

    public void Show(ComponentInfo component)
    {
        _component = component;
        Build();
        ShowRequested?.Invoke();
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    // Trocou o período no seletor → refaz os gráficos (os eixos mudam junto com a origem dos dados).
    partial void OnSelectedPeriodChanged(PeriodOption? value) => Build();

    private void Build()
    {
        if (_component is not { } component)
            return;

        // Uma consulta que ainda esteja em andamento era para os gráficos antigos: a resposta dela será descartada.
        _generation++;
        _reading = false;
        var range = SelectedPeriod?.Range;

        Title = component.Name;
        KindLabel = ComponentCardViewModel.LabelFor(component.Kind);
        Notice = "";
        Charts.Clear();

        var service = _feed.Sensors;
        var colors = ChartColors.Current;

        foreach (var group in DetailCharts.For(component, service.Sensors))
        {
            // A cor vem da posição na regra, antes de tirar os ocultos:
            // ocultar um sensor não muda a cor dos que ficam.
            var rows = group.Sensors
                .Select((chartSensor, index) => (chartSensor, Color: colors.Series[index]))
                .Where(x => !_prefs.IsHidden(x.chartSensor.Sensor.Id))
                .Select(x => new ChartRowViewModel(
                    x.chartSensor.Sensor,
                    _prefs.GetCustomName(x.chartSensor.Sensor.Id) ?? x.chartSensor.Label,
                    x.Color,
                    service.Latest))
                .ToList();

            if (rows.Count == 0)
                continue;

            if (range is null)
            {
                foreach (var row in rows)
                    row.ShowLive(service.History);
            }
            Charts.Add(new ChartViewModel(group, rows, colors, range));
        }

        // Histórico longo: os gráficos aparecem já com eixos e tabela, e as linhas chegam logo depois.
        if (range is not null)
            _ = LoadPeriodAsync(range);
    }

    /// <summary>
    /// Lê o período do banco fora da thread da interface e preenche os gráficos que estão na tela.
    /// Nenhuma exceção sai daqui (o método é chamado sem await): falha vira aviso na página.
    /// </summary>
    private async Task LoadPeriodAsync(HistoryRange range)
    {
        int generation = ++_generation;
        var charts = Charts.ToList();
        var ids = charts.SelectMany(chart => chart.Rows).Select(row => row.Sensor.Id).Distinct().ToList();
        var now = DateTimeOffset.UtcNow;

        _loadedAt = now;
        _reading = true;
        try
        {
            var data = await Task.Run(() =>
            {
                var series = new Dictionary<string, HistorySeries>();
                foreach (string id in ids)
                    series[id] = _store.ReadSeries(id, range, now);
                return series;
            });

            // Enquanto o banco era lido, o usuário trocou de período ou de componente: esta resposta não serve mais.
            if (generation != _generation)
                return;

            var (from, to) = range.Window(now);
            foreach (var chart in charts)
                chart.ShowPeriod(data, from, to);

            Notice = data.Values.Any(series => series.Points.Count > 0)
                ? ""
                : "Ainda não há histórico gravado neste período. O app grava um resumo por minuto enquanto está aberto.";
        }
        catch (Exception ex)
        {
            if (generation == _generation)
                Notice = $"Não foi possível ler o histórico: {ex.Message}";
        }
        finally
        {
            if (generation == _generation)
                _reading = false;
        }
    }

    private void OnUpdated(SensorSnapshot snapshot)
    {
        var history = _feed.Sensors.History;
        foreach (var chart in Charts)
            chart.Update(snapshot, history);

        // Histórico longo: de tempos em tempos busca de novo, para o minuto (ou a hora) que acabou de
        // ser gravado aparecer e a janela andar.
        if (IsActive && !_reading && _component is not null && SelectedPeriod?.Range is { } range
            && DateTimeOffset.UtcNow - _loadedAt >= RefreshEvery(range))
        {
            _ = LoadPeriodAsync(range);
        }
    }

    /// <summary>
    /// De quanto em quanto tempo o período é relido. Minutos: a cada minuto, que é quando chega um ponto
    /// novo. Horas: a cada 10 min (a hora em andamento vai mudando, mas devagar; a consulta de 1 ano é a maior).
    /// </summary>
    private static TimeSpan RefreshEvery(HistoryRange range)
        => range.Table == HistoryTable.Minute ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(10);
}