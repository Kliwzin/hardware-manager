using CoreWindows.Sensors;
using CoreWindows.Sensors.Nvidia;

namespace CoreWindows.Tests;

public class NvmlSensorSourceTests
{
    private const string GpuId = "/gpu-nvidia/0";
    private const string PowerId = "/gpu-nvidia/0/nvml/power";

    private static readonly ComponentInfo[] Components =
        [new(GpuId, "NVIDIA GeForce RTX 4060", ComponentKind.Gpu, null)];

    // O que o LHM mostra da RTX 4060 do PC 1: temperatura e carga, sem sensor de consumo.
    private static readonly SensorInfo[] LhmSensors =
    [
        new("/gpu-nvidia/0/temperature/0", GpuId, "GPU Core", SensorKind.Temperature),
        new("/gpu-nvidia/0/load/0", GpuId, "GPU Core", SensorKind.Load),
    ];

    /// <summary>GPU falsa: cada consulta devolve o que o teste mandar e conta quantas vezes foi chamada.</summary>
    private sealed class FakeGpu : INvmlGpu
    {
        public NvmlResult<double> Power = new(NvmlReturn.Success, 42.5);
        public NvmlResult<NvmlThrottleReasons> Reasons = new(NvmlReturn.Success, NvmlThrottleReasons.GpuIdle);
        public bool Throw;
        public int ReasonCalls;

        public string Name { get; init; } = "GeForce RTX 4060";

        public NvmlResult<double> ReadPowerWatts()
            => Throw ? throw new InvalidOperationException("falha simulada") : Power;

        public NvmlResult<NvmlThrottleReasons> ReadThrottleReasons()
        {
            ReasonCalls++;
            return Throw ? throw new InvalidOperationException("falha simulada") : Reasons;
        }
    }

    private sealed class Owner : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Consumo_vira_um_sensor_do_componente_que_o_lhm_ja_tem()
    {
        using var source = new NvmlSensorSource([new FakeGpu()], Components, LhmSensors);

        var sensor = Assert.Single(source.Sensors);
        Assert.Equal(new SensorInfo(PowerId, GpuId, "GPU Power", SensorKind.Power), sensor);
        Assert.Empty(source.Components);
        Assert.Equal(42.5, source.Read().Values[PowerId]);
    }

    [Fact]
    public void O_sensor_de_consumo_entra_no_grafico_Consumo_da_gpu()
    {
        using var source = new NvmlSensorSource([new FakeGpu()], Components, LhmSensors);

        var charts = DetailCharts.For(Components[0], [.. LhmSensors, .. source.Sensors]);

        var chart = Assert.Single(charts, c => c.Title == "Consumo");
        Assert.Equal(PowerId, Assert.Single(chart.Sensors).Sensor.Id);
    }

    [Fact]
    public void Placa_que_nao_informa_consumo_nao_ganha_sensor_mas_informa_os_motivos()
    {
        var gpu = new FakeGpu
        {
            Power = new(NvmlReturn.NotSupported, 0),
            Reasons = new(NvmlReturn.Success, NvmlThrottleReasons.SwThermalSlowdown),
        };
        using var source = new NvmlSensorSource([gpu], Components, LhmSensors);

        Assert.Empty(source.Sensors);
        Assert.False(source.IsEmpty);
        Assert.Empty(source.Read().Values);
        Assert.Equal(NvmlThrottleReasons.SwThermalSlowdown, source.ThrottleReasons[GpuId]);
    }

    [Fact]
    public void Nao_cria_consumo_repetido_quando_o_lhm_ja_tem()
    {
        SensorInfo[] sensors = [.. LhmSensors, new("/gpu-nvidia/0/power/0", GpuId, "GPU Package", SensorKind.Power)];

        using var source = new NvmlSensorSource([new FakeGpu()], Components, sensors);

        Assert.Empty(source.Sensors);
    }

    [Fact]
    public void Os_motivos_acompanham_cada_leitura()
    {
        var gpu = new FakeGpu();
        using var source = new NvmlSensorSource([gpu], Components, LhmSensors);
        Assert.Equal(NvmlThrottleReasons.GpuIdle, source.ThrottleReasons[GpuId]); // já na criação

        gpu.Reasons = new(NvmlReturn.Success, NvmlThrottleReasons.SwPowerCap | NvmlThrottleReasons.SwThermalSlowdown);
        source.Read();

        Assert.Equal(NvmlThrottleReasons.SwPowerCap | NvmlThrottleReasons.SwThermalSlowdown, source.ThrottleReasons[GpuId]);
    }

    [Fact]
    public void Leitura_que_falha_deixa_o_sensor_sem_valor_e_nao_lanca_excecao()
    {
        var gpu = new FakeGpu();
        using var source = new NvmlSensorSource([gpu], Components, LhmSensors);

        gpu.Power = new(NvmlReturn.GpuIsLost, 0);
        gpu.Reasons = new(NvmlReturn.GpuIsLost, default);
        Assert.Empty(source.Read().Values);
        Assert.Empty(source.ThrottleReasons);

        gpu.Throw = true;
        Assert.Empty(source.Read().Values);

        // Falha passageira não desliga nada: quando a placa volta, os dados voltam.
        gpu.Throw = false;
        gpu.Power = new(NvmlReturn.Success, 80);
        gpu.Reasons = new(NvmlReturn.Success, NvmlThrottleReasons.None);
        Assert.Equal(80, source.Read().Values[PowerId]);
        Assert.Equal(NvmlThrottleReasons.None, source.ThrottleReasons[GpuId]);
    }

    [Fact]
    public void Para_de_perguntar_os_motivos_quando_a_placa_diz_que_nao_suporta()
    {
        var gpu = new FakeGpu();
        using var source = new NvmlSensorSource([gpu], Components, LhmSensors);

        gpu.Reasons = new(NvmlReturn.NotSupported, default);
        source.Read();
        int calls = gpu.ReasonCalls;
        source.Read();
        source.Read();

        Assert.Equal(calls, gpu.ReasonCalls);
        Assert.Empty(source.ThrottleReasons);
    }

    [Fact]
    public void Placa_sem_consumo_e_sem_motivos_deixa_a_fonte_vazia()
    {
        var gpu = new FakeGpu
        {
            Power = new(NvmlReturn.NotSupported, 0),
            Reasons = new(NvmlReturn.FunctionNotFound, default),
        };
        using var source = new NvmlSensorSource([gpu], Components, LhmSensors);

        Assert.True(source.IsEmpty);
        Assert.Empty(source.Sensors);
    }

    [Fact]
    public void Gpu_que_o_lhm_nao_mostra_e_ignorada()
    {
        ComponentInfo[] onlyCpu = [new("/intelcpu/0", "Intel Core i5-12400F", ComponentKind.Cpu, null)];
        var gpu = new FakeGpu();

        using var source = new NvmlSensorSource([gpu], onlyCpu, []);

        Assert.True(source.IsEmpty);
        Assert.Equal(0, gpu.ReasonCalls); // nem chega a consultar a placa
    }

    [Fact]
    public void Dispose_fecha_a_sessao_do_nvml()
    {
        var owner = new Owner();

        new NvmlSensorSource([new FakeGpu()], Components, LhmSensors, owner).Dispose();

        Assert.True(owner.Disposed);
    }

    [Fact]
    public void Sem_driver_nvidia_a_sessao_nao_abre_e_explica_o_motivo()
    {
        // Um arquivo que não existe = PC sem driver NVIDIA. Não pode lançar exceção.
        var session = NvmlSession.TryOpen(out string? reason, Path.Combine(Path.GetTempPath(), "nao-existe", "nvml.dll"));

        Assert.Null(session);
        Assert.False(string.IsNullOrEmpty(reason));
    }
}