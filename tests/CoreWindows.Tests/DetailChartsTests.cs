using CoreWindows.Sensors;

namespace CoreWindows.Tests;

public class DetailChartsTests
{
    private static readonly ComponentInfo Cpu = new("/intelcpu/0", "i5-12400F", ComponentKind.Cpu, null);
    private static readonly ComponentInfo Gpu = new("/gpu-nvidia/0", "RTX 4060", ComponentKind.Gpu, null);

    private static SensorInfo Sensor(ComponentInfo component, SensorKind kind, int index, string name)
        => new($"{component.Id}/{kind.ToString().ToLowerInvariant()}/{index}", component.Id, name, kind);

    [Fact]
    public void Gpu_do_pc1_tem_um_grafico_por_tipo_na_ordem_das_regras()
    {
        // Nomes reais do relatório docs/sensores (RTX 4060): não há sensor de consumo.
        SensorInfo[] sensors =
        [
            Sensor(Gpu, SensorKind.Clock, 0, "GPU Core"),
            Sensor(Gpu, SensorKind.Clock, 1, "GPU Memory"),
            Sensor(Gpu, SensorKind.Temperature, 0, "GPU Core"),
            Sensor(Gpu, SensorKind.Temperature, 1, "GPU Hot Spot"),
            Sensor(Gpu, SensorKind.Load, 0, "GPU Core"),
            Sensor(Gpu, SensorKind.Load, 1, "GPU Memory Controller"),
            Sensor(Gpu, SensorKind.Load, 2, "GPU Video Engine"),
            Sensor(Gpu, SensorKind.Load, 3, "GPU Bus"),
            Sensor(Gpu, SensorKind.Fan, 0, "GPU Fan 1"),
            Sensor(Gpu, SensorKind.Fan, 1, "GPU Fan 2"),
            Sensor(Gpu, SensorKind.Control, 0, "GPU Fan 1"),
            Sensor(Gpu, SensorKind.SmallData, 0, "GPU Memory Free"),
            Sensor(Gpu, SensorKind.SmallData, 1, "GPU Memory Used"),
        ];

        var charts = DetailCharts.For(Gpu, sensors);

        Assert.Equal(
            new[] { "Utilização", "Temperatura", "Clock", "VRAM", "Ventoinhas" },
            charts.Select(c => c.Title));

        var load = charts[0];
        Assert.Equal(SensorKind.Load, load.Kind);
        Assert.Equal(new[] { "GPU", "Controlador de memória", "Vídeo" }, load.Sensors.Select(s => s.Label));
        Assert.Equal("/gpu-nvidia/0/load/0", load.Sensors[0].Sensor.Id); // "GPU Core" de carga, não o de clock

        var fans = charts[4];
        Assert.Equal(new[] { "Ventoinha 1", "Ventoinha 2" }, fans.Sensors.Select(s => s.Label));
        Assert.Equal("/gpu-nvidia/0/fan/0", fans.Sensors[0].Sensor.Id); // RPM, não o controle em %
    }

    [Fact]
    public void Clock_da_cpu_tem_uma_linha_por_nucleo()
    {
        SensorInfo[] sensors =
        [
            Sensor(Cpu, SensorKind.Clock, 0, "Bus Speed"),
            Sensor(Cpu, SensorKind.Clock, 1, "CPU Core #1"),
            Sensor(Cpu, SensorKind.Clock, 2, "CPU Core #2"),
            Sensor(Cpu, SensorKind.Temperature, 0, "CPU Core #1"),
            Sensor(Cpu, SensorKind.Temperature, 1, "CPU Core #1 Distance to TjMax"),
        ];

        var clock = Assert.Single(DetailCharts.For(Cpu, sensors)); // temperatura por núcleo não tem regra

        Assert.Equal("Clock", clock.Title);
        Assert.Equal(new[] { "Núcleo 1", "Núcleo 2" }, clock.Sensors.Select(s => s.Label));
    }

    [Fact]
    public void Grafico_tem_no_maximo_oito_linhas()
    {
        var sensors = Enumerable.Range(1, 16)
            .Select(i => Sensor(Cpu, SensorKind.Clock, i, $"CPU Core #{i}"))
            .ToList();

        var clock = Assert.Single(DetailCharts.For(Cpu, sensors));

        Assert.Equal(DetailCharts.MaxSeries, clock.Sensors.Count);
        Assert.Equal("Núcleo 8", clock.Sensors[^1].Label);
    }

    [Fact]
    public void Vale_o_primeiro_nome_encontrado()
    {
        var nvme = new ComponentInfo("/nvme/0", "TEAM TM8FPD001T", ComponentKind.Storage, null);
        SensorInfo[] sensors =
        [
            Sensor(nvme, SensorKind.Temperature, 0, "Composite Temperature"),
            Sensor(nvme, SensorKind.Temperature, 1, "Temperature"),
        ];

        var temperature = Assert.Single(DetailCharts.For(nvme, sensors));

        var only = Assert.Single(temperature.Sensors);
        Assert.Equal("/nvme/0/temperature/0", only.Sensor.Id);
    }

    [Fact]
    public void Ignora_sensores_de_outro_componente_e_componentes_sem_regra()
    {
        var superIo = new ComponentInfo("/lpc/nct6798d/0", "Nuvoton", ComponentKind.SuperIO, "/motherboard");
        SensorInfo[] sensors = [Sensor(Gpu, SensorKind.Load, 0, "GPU Core")];

        Assert.Empty(DetailCharts.For(Cpu, sensors));
        Assert.Empty(DetailCharts.For(superIo, sensors));
    }
}
