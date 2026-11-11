using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreWindows.Sensors;
using HardwareManager.App.Services;

namespace HardwareManager.App.ViewModels;

/// <summary>Tela de detalhe de um componente: gráficos dos últimos 30 min + valores da sessão.</summary>
public sealed partial class DetailViewModel : ObservableObject
{
    private readonly UiSensorFeed _feed;
    private readonly SensorPreferences _prefs;
    private ComponentInfo? _component;

    public ObservableCollection<ChartViewModel> Charts { get; } = [];

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _kindLabel = "";

    /// <summary>Pede à janela principal para mostrar a página de detalhe.</summary>
    public event Action? ShowRequested;

    /// <summary>Pede à janela principal para voltar à visão geral.</summary>
    public event Action? BackRequested;

    public DetailViewModel(UiSensorFeed feed, SensorPreferences prefs)
    {
        _feed = feed;
        _prefs = prefs;
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

    private void Build()
    {
        if (_component is not { } component)
            return;

        Title = component.Name;
        KindLabel = ComponentCardViewModel.LabelFor(component.Kind);
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
                    service.History,
                    service.Latest))
                .ToList();

            if (rows.Count > 0)
                Charts.Add(new ChartViewModel(group, rows, colors));
        }
    }

    private void OnUpdated(SensorSnapshot snapshot)
    {
        var history = _feed.Sensors.History;
        foreach (var chart in Charts)
            chart.Update(snapshot, history);
    }
}
