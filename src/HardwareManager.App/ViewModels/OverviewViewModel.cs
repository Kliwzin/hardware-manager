using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreWindows.Sensors;
using HardwareManager.App.Services;

namespace HardwareManager.App.ViewModels;

public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly UiSensorFeed _feed;
    private readonly SensorPreferences _prefs;
    private readonly DetailViewModel _detail;

    public ObservableCollection<ComponentCardViewModel> Cards { get; } = [];

    public OverviewViewModel(UiSensorFeed feed, SensorPreferences prefs, DetailViewModel detail)
    {
        _feed = feed;
        _prefs = prefs;
        _detail = detail;
        _feed.Updated += OnUpdated;
        _prefs.Changed += Build; // renomeou/ocultou na página Sensores → refaz os cards
    }

    /// <summary>Monta os cards. Chamado quando os sensores ficam prontos.</summary>
    public void Build()
    {
        Cards.Clear();
        var sensors = _feed.Sensors.Sensors;

        foreach (var component in _feed.Sensors.Components)
        {
            var values = KeySensors.For(component, sensors)
                .Where(k => !_prefs.IsHidden(k.Sensor.Id))
                .Select(k => new SensorValueViewModel(k.Sensor, _prefs.GetCustomName(k.Sensor.Id) ?? k.Label))
                .ToList();

            if (values.Count > 0)
                Cards.Add(new ComponentCardViewModel(component, values));
        }

        if (_feed.Sensors.Latest is { } latest)
            OnUpdated(latest);
    }

    /// <summary>Clique num card → tela de detalhe daquele componente.</summary>
    [RelayCommand]
    private void OpenDetail(ComponentCardViewModel card) => _detail.Show(card.Component);

    private void OnUpdated(SensorSnapshot snapshot)
    {
        foreach (var card in Cards)
            foreach (var value in card.Values)
                value.Update(snapshot);
    }
}