using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CoreWindows.Sensors;
using HardwareManager.App.Services;

namespace HardwareManager.App.ViewModels;

public sealed class OverviewViewModel : ObservableObject
{
    private readonly UiSensorFeed _feed;
    private readonly SensorPreferences _prefs;

    public ObservableCollection<ComponentCardViewModel> Cards { get; } = [];

    public OverviewViewModel(UiSensorFeed feed, SensorPreferences prefs)
    {
        _feed = feed;
        _prefs = prefs;
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

    private void OnUpdated(SensorSnapshot snapshot)
    {
        foreach (var card in Cards)
            foreach (var value in card.Values)
                value.Update(snapshot);
    }
}