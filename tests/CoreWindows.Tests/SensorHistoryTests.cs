using CoreWindows.Sensors;

namespace CoreWindows.Tests;

public class SensorHistoryTests
{
    private const string Cpu = "/intelcpu/0/temperature/6";
    private const string Gpu = "/gpu-nvidia/0/temperature/0";

    private static readonly DateTimeOffset Start = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Leitura feita "second" segundos depois do início, com os valores informados.</summary>
    private static SensorSnapshot Snap(int second, params (string Id, double Value)[] values)
        => new(Start.AddSeconds(second), values.ToDictionary(v => v.Id, v => v.Value));

    private static double?[] Values(SensorHistory history, string id)
        => history.GetSeries(id).Select(p => p.Value).ToArray();

    [Fact]
    public void Historico_vazio_nao_tem_pontos_nem_estatisticas()
    {
        var history = new SensorHistory(10);

        Assert.Equal(0, history.Count);
        Assert.Empty(history.GetSeries(Cpu));
        Assert.Null(history.GetStats(Cpu));
    }

    [Fact]
    public void Guarda_as_leituras_em_ordem_com_o_horario()
    {
        var history = new SensorHistory(10);
        history.Add(Snap(0, (Cpu, 30)));
        history.Add(Snap(1, (Cpu, 31)));
        history.Add(Snap(2, (Cpu, 32)));

        var series = history.GetSeries(Cpu);

        Assert.Equal(3, history.Count);
        Assert.Equal(new double?[] { 30, 31, 32 }, series.Select(p => p.Value));
        Assert.Equal(Start, series[0].Timestamp);
        Assert.Equal(Start.AddSeconds(2), series[2].Timestamp);
    }

    [Fact]
    public void Cheio_descarta_as_leituras_mais_antigas()
    {
        var history = new SensorHistory(3);
        for (int i = 0; i < 5; i++)
            history.Add(Snap(i, (Cpu, 30 + i)));

        var series = history.GetSeries(Cpu);

        Assert.Equal(3, history.Count);
        Assert.Equal(new double?[] { 32, 33, 34 }, series.Select(p => p.Value));
        Assert.Equal(Start.AddSeconds(2), series[0].Timestamp);
    }

    [Fact]
    public void Sensor_sem_valor_numa_leitura_vira_lacuna()
    {
        var history = new SensorHistory(2);
        history.Add(Snap(0, (Cpu, 30), (Gpu, 40)));
        history.Add(Snap(1, (Cpu, 31), (Gpu, 41)));
        history.Add(Snap(2, (Gpu, 42))); // grava por cima da posição que tinha Cpu = 30

        Assert.Equal(new double?[] { 31, null }, Values(history, Cpu));
        Assert.Equal(new double?[] { 41, 42 }, Values(history, Gpu));
    }

    [Fact]
    public void Sensor_que_aparece_depois_tem_lacunas_no_inicio()
    {
        // Caso real: a carga da CPU só tem valor a partir da segunda leitura.
        var history = new SensorHistory(10);
        history.Add(Snap(0, (Gpu, 40)));
        history.Add(Snap(1, (Gpu, 41), (Cpu, 31)));

        Assert.Equal(new double?[] { null, 31 }, Values(history, Cpu));
    }

    [Fact]
    public void Valores_invalidos_sao_ignorados()
    {
        var history = new SensorHistory(10);
        history.Add(Snap(0, (Cpu, 30)));
        history.Add(Snap(1, (Cpu, double.NaN)));
        history.Add(Snap(2, (Cpu, double.PositiveInfinity)));

        Assert.Equal(new double?[] { 30, null, null }, Values(history, Cpu));
        Assert.Equal(new SensorStats(30, 30, 30, 1), history.GetStats(Cpu));
    }

    [Fact]
    public void Estatisticas_valem_para_a_sessao_inteira()
    {
        var history = new SensorHistory(2);
        history.Add(Snap(0, (Cpu, 90))); // vai sair do buffer, mas continua sendo o máximo da sessão
        history.Add(Snap(1, (Cpu, 30)));
        history.Add(Snap(2, (Cpu, 40)));
        history.Add(Snap(3, (Cpu, 40)));

        Assert.Equal(new double?[] { 40, 40 }, Values(history, Cpu));
        Assert.Equal(new SensorStats(Min: 30, Max: 90, Average: 50, Samples: 4), history.GetStats(Cpu));
    }

    [Fact]
    public void Valor_recusado_pelo_filtro_fica_no_historico_mas_fora_das_estatisticas()
    {
        // Filtro de exemplo: só conta valor positivo (como a regra de temperatura plausível).
        var history = new SensorHistory(10, (_, value) => value > 0);
        history.Add(Snap(0, (Cpu, 30), (Gpu, -14)));
        history.Add(Snap(1, (Cpu, -14), (Gpu, -14)));
        history.Add(Snap(2, (Cpu, 40), (Gpu, -14)));

        Assert.Equal(new double?[] { 30, -14, 40 }, Values(history, Cpu));
        Assert.Equal(new SensorStats(Min: 30, Max: 40, Average: 35, Samples: 2), history.GetStats(Cpu));

        Assert.Equal(new double?[] { -14, -14, -14 }, Values(history, Gpu));
        Assert.Null(history.GetStats(Gpu)); // nunca teve um valor que conte
    }

    [Fact]
    public void Capacidade_vem_da_janela_e_do_intervalo()
    {
        Assert.Equal(1800, SensorHistory.CapacityFor(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1)));
        Assert.Equal(258, SensorHistory.CapacityFor(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(7))); // arredonda para cima
        Assert.Equal(1, SensorHistory.CapacityFor(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Capacidade_invalida_lanca_excecao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SensorHistory(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SensorHistory.CapacityFor(TimeSpan.FromMinutes(30), TimeSpan.Zero));
    }

    [Fact]
    public async Task Leitura_e_escrita_ao_mesmo_tempo_nao_corrompem_o_historico()
    {
        var history = new SensorHistory(100);
        const int total = 20_000;

        var writer = Task.Run(() =>
        {
            for (int i = 0; i < total; i++)
                history.Add(Snap(i, (Cpu, i)));
        });

        var reader = Task.Run(() =>
        {
            int broken = 0;
            while (!writer.IsCompleted)
            {
                // Cada cópia tem de ser uma sequência contínua: valor = segundo, sempre de 1 em 1.
                var series = history.GetSeries(Cpu);
                for (int i = 1; i < series.Count; i++)
                {
                    if (series[i].Value != series[i - 1].Value + 1 ||
                        series[i].Timestamp != series[i - 1].Timestamp.AddSeconds(1))
                        broken++;
                }
            }
            return broken;
        });

        await writer;
        Assert.Equal(0, await reader);
        Assert.Equal(100, history.Count);
        Assert.Equal(19_999.0, history.GetSeries(Cpu)[^1].Value);
    }
}
