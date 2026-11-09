using CoreWindows.Sensors;

namespace CoreWindows.Tests;

public class KeySensorsTests
{
    private static readonly ComponentInfo Gpu = new("/gpu-nvidia/0", "RTX 4060", ComponentKind.Gpu, null);

    [Fact]
    public void Diferencia_sensores_de_mesmo_nome_pelo_tipo()
    {
        SensorInfo[] sensors =
        [
            new("/gpu-nvidia/0/clock/0", Gpu.Id, "GPU Core", SensorKind.Clock),
            new("/gpu-nvidia/0/temperature/0", Gpu.Id, "GPU Core", SensorKind.Temperature),
            new("/gpu-nvidia/0/load/0", Gpu.Id, "GPU Core", SensorKind.Load),
        ];

        var keys = KeySensors.For(Gpu, sensors);

        Assert.Equal(new[] { "Uso", "Temperatura", "Clock" }, keys.Select(k => k.Label));
        Assert.Equal("/gpu-nvidia/0/load/0", keys[0].Sensor.Id);
    }

    [Fact]
    public void Componente_sem_regra_nao_tem_destaques()
    {
        var superIo = new ComponentInfo("/lpc/nct6798d/0", "Nuvoton", ComponentKind.SuperIO, "/motherboard");
        Assert.Empty(KeySensors.For(superIo, []));
    }
}