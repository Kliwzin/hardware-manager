using System.Globalization;

namespace CoreWindows.Sensors;

/// <summary>Converte valores brutos em texto legível com unidade.</summary>
public static class SensorFormat
{
    public static string Unit(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => "°C",
        SensorKind.Load or SensorKind.Control or SensorKind.Level => "%",
        SensorKind.Clock => "MHz",
        SensorKind.Power => "W",
        SensorKind.Voltage => "V",
        SensorKind.Current => "A",
        SensorKind.Fan => "RPM",
        SensorKind.Data => "GB",
        SensorKind.SmallData => "MB",
        SensorKind.Throughput => "B/s",
        SensorKind.Frequency => "Hz",
        SensorKind.Flow => "L/h",
        SensorKind.Timing => "ns",
        _ => ""
    };

    public static string Format(SensorKind kind, double value, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;

        if (kind == SensorKind.Throughput)
            return FormatRate(value, provider);

        string number = kind switch
        {
            SensorKind.Fan or SensorKind.Clock or SensorKind.Factor or SensorKind.SmallData
                => value.ToString("0", provider),
            SensorKind.Voltage => value.ToString("0.000", provider),
            _ => value.ToString("0.#", provider)
        };

        string unit = Unit(kind);
        return unit.Length == 0 ? number : $"{number} {unit}";
    }

    private static string FormatRate(double bytesPerSecond, IFormatProvider provider)
    {
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        int i = 0;
        while (bytesPerSecond >= 1024 && i < units.Length - 1)
        {
            bytesPerSecond /= 1024;
            i++;
        }
        return $"{bytesPerSecond.ToString(i == 0 ? "0" : "0.#", provider)} {units[i]}";
    }
}