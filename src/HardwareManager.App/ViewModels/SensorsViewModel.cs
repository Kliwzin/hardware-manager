using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HardwareManager.App.Services;

namespace HardwareManager.App.ViewModels;

public sealed class SensorsViewModel : ObservableObject
{
    private readonly UiSensorFeed _feed;
    private readonly SensorPreferences _prefs;

    public ObservableCollection<SensorRowViewModel> Rows { get; } = [];

    public SensorsViewModel(UiSensorFeed feed, SensorPreferences prefs)
    {
        _feed = feed;
        _prefs = prefs;
        _feed.Updated += snapshot =>
        {
            foreach (var row in Rows)
                row.Update(snapshot);
        };
    }

    public void Build()
    {
        Rows.Clear();
        var components = _feed.Sensors.Components.ToDictionary(c => c.Id);

        foreach (var sensor in _feed.Sensors.Sensors)
            Rows.Add(new SensorRowViewModel(components[sensor.ComponentId], sensor, _prefs));

        if (_feed.Sensors.Latest is { } latest)
            foreach (var row in Rows)
                row.Update(latest);
    }
}