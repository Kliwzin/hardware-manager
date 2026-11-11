using CommunityToolkit.Mvvm.ComponentModel;
using CoreWindows.Sensors;
using HardwareManager.App.Services;

namespace HardwareManager.App.ViewModels;

/// <summary>Uma linha da página Sensores: valor ao vivo + nome personalizado + oculto.</summary>
public sealed partial class SensorRowViewModel : SensorValueViewModel
{
    private readonly SensorPreferences _prefs;
    private readonly bool _initialized;

    public SensorRowViewModel(ComponentInfo component, SensorInfo sensor, SensorPreferences prefs)
        : base(sensor, sensor.Name)
    {
        _prefs = prefs;
        ComponentName = component.Name;
        CustomName = prefs.GetCustomName(sensor.Id) ?? "";
        IsHidden = prefs.IsHidden(sensor.Id);
        _initialized = true; // a partir daqui, mudanças vêm do usuário e devem ser salvas
    }

    public string ComponentName { get; }

    [ObservableProperty] private string _customName = "";
    [ObservableProperty] private bool _isHidden;

    partial void OnCustomNameChanged(string value)
    {
        if (_initialized) _prefs.SetCustomName(Sensor.Id, value);
    }

    partial void OnIsHiddenChanged(bool value)
    {
        if (_initialized) _prefs.SetHidden(Sensor.Id, value);
    }
}