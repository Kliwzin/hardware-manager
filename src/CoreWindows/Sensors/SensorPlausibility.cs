namespace CoreWindows.Sensors;

/// <summary>Detecta leituras fisicamente absurdas (ex.: entrada de temperatura sem nada ligado).</summary>
public static class SensorPlausibility
{
    public static bool IsPlausible(SensorKind kind, double value) => kind switch
    {
        SensorKind.Temperature => value is > 0 and < 125,
        SensorKind.Load or SensorKind.Control or SensorKind.Level => value is >= 0 and <= 100,
        SensorKind.Fan => value is >= 0 and < 20_000,
        SensorKind.Voltage => value is >= 0 and < 20,
        _ => true
    };
}