using CoreWindows.Sensors;
using CoreWindows.Storage;

namespace CoreWindows.Tests;

/// <summary>Períodos do histórico longo e montagem das séries. Tudo sem banco: as linhas são inventadas.</summary>
public sealed class HistoryRangeTests
{
    // 12:00 UTC: começo exato de uma hora e de um intervalo de 6 h.
    private static readonly DateTimeOffset Noon = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    private static HistoryRow Minute(int minutes, double average, double min, double max, int samples = 60)
        => new(Noon.AddMinutes(minutes), average, min, max, samples);

    private static HistoryRow Hour(int hours, double average, double min, double max, int samples = 3600)
        => new(Noon.AddHours(hours), average, min, max, samples);

    [Fact]
    public void Janela_termina_no_fim_do_ponto_em_andamento()
    {
        var (from, to) = HistoryRange.Day.Window(Noon.AddMinutes(34).AddSeconds(56)); // 12:34:56

        Assert.Equal(Noon.AddMinutes(35), to);                 // o minuto 12:34 entra inteiro
        Assert.Equal(Noon.AddMinutes(35).AddHours(-24), from);
    }

    [Fact]
    public void Janela_nao_se_mexe_enquanto_o_ponto_atual_nao_vira()
    {
        // 1 ano usa pontos de 6 h: das 12:00 às 17:59 a janela é a mesma.
        var first = HistoryRange.Year.Window(Noon);
        var later = HistoryRange.Year.Window(Noon.AddHours(5).AddMinutes(59));
        var next = HistoryRange.Year.Window(Noon.AddHours(6));

        Assert.Equal(Noon.AddHours(6), first.To);
        Assert.Equal(first, later);
        Assert.Equal(Noon.AddHours(12), next.To);
    }

    [Fact]
    public void Todo_periodo_cabe_em_ate_1500_pontos_e_usa_ponto_multiplo_da_linha_da_tabela()
    {
        foreach (var range in HistoryRange.All)
        {
            var row = range.Table == HistoryTable.Minute ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1);

            Assert.Equal(0, range.Bucket.Ticks % row.Ticks);
            Assert.Equal(0, range.Span.Ticks % range.Bucket.Ticks);
            Assert.InRange(range.Span / range.Bucket, 100, 1500);
        }

        // Minutos só existem por 7 dias: nenhum período maior que isso pode depender deles.
        Assert.All(HistoryRange.All.Where(r => r.Table == HistoryTable.Minute),
            r => Assert.True(r.Span <= HistoryStore.DefaultMinuteRetention));
        Assert.All(HistoryRange.All, r => Assert.True(r.Span <= HistoryStore.DefaultHourRetention));
    }

    [Fact]
    public void Linhas_seguidas_viram_pontos_seguidos_sem_buraco()
    {
        var series = HistorySeries.From([Minute(0, 30, 29, 31), Minute(1, 32, 31, 33), Minute(2, 34, 33, 35)], OneMinute);

        HistoryPoint[] expected =
            [new(Noon, 30), new(Noon.AddMinutes(1), 32), new(Noon.AddMinutes(2), 34)];
        Assert.Equal<HistoryPoint>(expected, series.Points);
    }

    [Fact]
    public void Trecho_sem_dados_vira_um_unico_ponto_nulo_logo_depois_do_ultimo_valor()
    {
        // PC desligado das 12:02 às 12:59.
        var series = HistorySeries.From([Minute(0, 30, 30, 30), Minute(1, 31, 31, 31), Minute(60, 40, 40, 40)], OneMinute);

        HistoryPoint[] expected =
        [
            new(Noon, 30),
            new(Noon.AddMinutes(1), 31),
            new(Noon.AddMinutes(2), null), // a linha do gráfico quebra aqui
            new(Noon.AddMinutes(60), 40),
        ];
        Assert.Equal<HistoryPoint>(expected, series.Points);
    }

    [Fact]
    public void Linhas_do_mesmo_intervalo_sao_juntadas_com_media_ponderada()
    {
        // Pontos de 6 h: 12:00–17:59 e 18:00–23:59. A hora das 13:00 só teve 1200 leituras.
        var series = HistorySeries.From(
            [Hour(0, 30, 20, 35), Hour(1, 60, 50, 90, samples: 1200), Hour(6, 45, 44, 46)],
            TimeSpan.FromHours(6));

        Assert.Equal(2, series.Points.Count);
        Assert.Equal(Noon, series.Points[0].Timestamp);
        Assert.Equal(37.5, series.Points[0].Value!.Value, precision: 9); // (30×3600 + 60×1200) ÷ 4800
        Assert.Equal(new HistoryPoint(Noon.AddHours(6), 45), series.Points[1]);
    }

    [Fact]
    public void Resumo_do_periodo_usa_minimo_e_maximo_verdadeiros()
    {
        // O pico de 97 durou segundos: some na média do minuto, mas continua no máximo.
        var series = HistorySeries.From([Minute(0, 30, 28, 33, samples: 20), Minute(1, 60, 55, 97, samples: 10)], OneMinute);

        var stats = series.Stats!.Value;
        Assert.Equal(28, stats.Min);
        Assert.Equal(97, stats.Max);
        Assert.Equal(40, stats.Average, precision: 9); // (30×20 + 60×10) ÷ 30
        Assert.Equal(30, stats.Samples);
    }

    [Fact]
    public void Sem_linhas_ou_so_com_linhas_vazias_a_serie_e_vazia()
    {
        Assert.Same(HistorySeries.Empty, HistorySeries.From([], OneMinute));
        Assert.Same(HistorySeries.Empty, HistorySeries.From([Minute(0, 30, 30, 30, samples: 0)], OneMinute));

        Assert.Empty(HistorySeries.Empty.Points);
        Assert.Null(HistorySeries.Empty.Stats);
    }
}