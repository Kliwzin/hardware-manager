using CoreWindows.Sensors;

namespace CoreWindows.Tests;

public class SensorRulesTests
{
    [Theory]
    [InlineData(SensorKind.Temperature, -14.0, false)] // Temperature #5 da placa-mãe: nada conectado
    [InlineData(SensorKind.Temperature, 36.0, true)]
    [InlineData(SensorKind.Temperature, 130.0, false)]
    [InlineData(SensorKind.Load, 47.27, true)]
    [InlineData(SensorKind.Load, 101.0, false)]
    public void Classifica_plausibilidade(SensorKind kind, double value, bool expected)
        => Assert.Equal(expected, SensorPlausibility.IsPlausible(kind, value));

    [Fact]
    public void Extrai_guid_do_id_de_rede()
        => Assert.Equal(
            "{A6A6D982-0E07-4372-B65E-BE91B7777B0F}",
            PhysicalNetworkAdapters.GuidFromIdentifier("/nic/%7BA6A6D982-0E07-4372-B65E-BE91B7777B0F%7D"));

    [Fact]
    public void Id_que_nao_e_de_rede_retorna_null()
        => Assert.Null(PhysicalNetworkAdapters.GuidFromIdentifier("/intelcpu/0"));
}