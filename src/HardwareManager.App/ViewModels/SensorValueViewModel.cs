using CommunityToolkit.Mvvm.ComponentModel;
using CoreWindows.Sensors;
using static System.Net.Mime.MediaTypeNames;

namespace HardwareManager.App.ViewModels;

/// <summary>Um valor ao vivo de um sensor, já formatado para exibição.</summary>
public partial class SensorValueViewModel(SensorInfo sensor, string label) : ObservableObject
{
    public SensorInfo Sensor { get; } = sensor;

    [ObservableProperty] private string _label = label;
    [ObservableProperty] private string _text = "—";
    [ObservableProperty] private bool _isSuspect;

    public void Update(SensorSnapshot snapshot)
    {
        if (snapshot.Values.TryGetValue(Sensor.Id, out double value))
        {
            Text = SensorFormat.Format(Sensor.Kind, value);
            IsSuspect = !SensorPlausibility.IsPlausible(Sensor.Kind, value);
        }
        else
        {
            Text = "—";
            IsSuspect = false;
        }
    }
}