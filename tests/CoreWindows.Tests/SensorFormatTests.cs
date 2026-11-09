using System.Globalization;
using CoreWindows.Sensors;

namespace CoreWindows.Tests;

public class SensorFormatTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(SensorKind.Temperature, 46.94, "46.9 °C")]
    [InlineData(SensorKind.Clock, 4393.16, "4393 MHz")]
    [InlineData(SensorKind.Fan, 1051.4, "1051 RPM")]
    [InlineData(SensorKind.Voltage, 1.09, "1.090 V")]
    [InlineData(SensorKind.Throughput, 512.0, "512 B/s")]
    [InlineData(SensorKind.Throughput, 1289250000.0, "1.2 GB/s")]
    [InlineData(SensorKind.Factor, 30032.0, "30032")]
    public void Formata_valor_com_unidade(SensorKind kind, double value, string expected)
        => Assert.Equal(expected, SensorFormat.Format(kind, value, Inv));
}