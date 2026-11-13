using CoreWindows.Sensors;
using CoreWindows.Storage;

namespace CoreWindows.Tests;

public class MinuteAggregatorTests
{
    private const string Cpu = "/intelcpu/0/temperature/6";
    private const string Gpu = "/gpu-nvidia/0/temperature/0";

    // 12:00:00 UTC, começo exato de um minuto.
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static SensorSnapshot Snap(int second, params (string Id, double Value)[] values)
        => new(Start.AddSeconds(second), values.ToDictionary(v => v.Id, v => v.Value));

    [Fact]
    public void Dentro_do_mesmo_minuto_nao_devolve_nada()
    {
        var aggregator = new MinuteAggregator();

        Assert.Null(aggregator.Add(Snap(0, (Cpu, 30))));
        Assert.Null(aggregator.Add(Snap(30, (Cpu, 31))));
        Assert.Null(aggregator.Add(Snap(59, (Cpu, 32))));
    }

    [Fact]
    public void Primeira_leitura_do_minuto_seguinte_fecha_o_anterior()
    {
        var aggregator = new MinuteAggregator();
        aggregator.Add(Snap(0, (Cpu, 30), (Gpu, 40)));
        aggregator.Add(Snap(1, (Cpu, 50), (Gpu, 40)));
        aggregator.Add(Snap(2, (Cpu, 40)));

        var batch = aggregator.Add(Snap(60, (Cpu, 99)));

        Assert.NotNull(batch);
        Assert.Equal(Start, batch.Start);
        Assert.Equal(MinuteAggregator.MinuteOf(Start), batch.Minute);
        Assert.Equal(new SensorAggregate(Cpu, Average: 40, Min: 30, Max: 50, Samples: 3), batch.Sensors.Single(s => s.SensorId == Cpu));
        Assert.Equal(new SensorAggregate(Gpu, Average: 40, Min: 40, Max: 40, Samples: 2), batch.Sensors.Single(s => s.SensorId == Gpu));
    }

    [Fact]
    public void Cada_minuto_comeca_do_zero()
    {
        var aggregator = new MinuteAggregator();
        aggregator.Add(Snap(0, (Cpu, 90)));
        aggregator.Add(Snap(60, (Cpu, 30)));

        var second = aggregator.Add(Snap(120, (Cpu, 10)));

        Assert.Equal(new SensorAggregate(Cpu, 30, 30, 30, 1), Assert.Single(second!.Sensors));
        Assert.Equal(Start.AddMinutes(1), second.Start);
    }

    [Fact]
    public void Leitura_recusada_pelo_filtro_e_valor_invalido_ficam_de_fora()
    {
        // Filtro de exemplo: só entra valor positivo (como a regra de temperatura plausível).
        var aggregator = new MinuteAggregator((_, value) => value > 0);
        aggregator.Add(Snap(0, (Cpu, 30), (Gpu, -14)));
        aggregator.Add(Snap(1, (Cpu, -14), (Gpu, -14)));
        aggregator.Add(Snap(2, (Cpu, double.NaN), (Gpu, -14)));

        var batch = aggregator.Flush();

        // A GPU só teve leituras recusadas: não vira linha no banco.
        Assert.Equal(new SensorAggregate(Cpu, 30, 30, 30, 1), Assert.Single(batch!.Sensors));
    }

    [Fact]
    public void Flush_devolve_o_minuto_incompleto_uma_vez_so()
    {
        var aggregator = new MinuteAggregator();
        Assert.Null(aggregator.Flush()); // nada acumulado ainda

        aggregator.Add(Snap(0, (Cpu, 30)));
        aggregator.Add(Snap(1, (Cpu, 32)));

        var batch = aggregator.Flush();

        Assert.Equal(new SensorAggregate(Cpu, 31, 30, 32, 2), Assert.Single(batch!.Sensors));
        Assert.Null(aggregator.Flush());
    }

    [Fact]
    public void Relogio_voltando_atras_tambem_fecha_o_minuto()
    {
        var aggregator = new MinuteAggregator();
        aggregator.Add(Snap(120, (Cpu, 30)));

        var batch = aggregator.Add(Snap(0, (Cpu, 40))); // o Windows acertou o relógio para trás

        Assert.Equal(Start.AddMinutes(2), batch!.Start);
        Assert.Equal(Start, aggregator.Flush()!.Start);
    }
}