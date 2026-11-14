using CoreWindows.Sensors;

namespace CoreWindows.Storage;

/// <summary>De qual tabela do banco um período é lido.</summary>
public enum HistoryTable { Minute, Hour }

/// <summary>
/// Um período do histórico longo e a forma de mostrá-lo: de qual tabela vêm os dados e quanto tempo
/// cada ponto do gráfico resume. A ideia é ficar sempre na faixa de centenas a ~1500 pontos por linha.
/// </summary>
/// <param name="Label">Texto curto do seletor de período ("24 h").</param>
/// <param name="Description">Texto por extenso para a legenda do gráfico.</param>
/// <param name="Span">Tamanho do período.</param>
/// <param name="Table">Tabela lida: minutos (guardados por 7 dias) ou horas (guardadas por 1 ano).</param>
/// <param name="Bucket">Tempo resumido em cada ponto. Precisa ser múltiplo da linha da tabela (1 min ou 1 h).</param>
public sealed record HistoryRange(string Label, string Description, TimeSpan Span, HistoryTable Table, TimeSpan Bucket)
{
    /// <summary>1440 pontos por linha.</summary>
    public static readonly HistoryRange Day = new(
        "24 h", "últimas 24 horas, um ponto por minuto",
        TimeSpan.FromHours(24), HistoryTable.Minute, TimeSpan.FromMinutes(1));

    /// <summary>168 pontos. Vem das horas, e não dos minutos, para não depender do prazo de retenção deles.</summary>
    public static readonly HistoryRange Week = new(
        "7 dias", "últimos 7 dias, um ponto por hora",
        TimeSpan.FromDays(7), HistoryTable.Hour, TimeSpan.FromHours(1));

    /// <summary>720 pontos.</summary>
    public static readonly HistoryRange Month = new(
        "30 dias", "últimos 30 dias, um ponto por hora",
        TimeSpan.FromDays(30), HistoryTable.Hour, TimeSpan.FromHours(1));

    /// <summary>1460 pontos (8760 horas juntadas de 6 em 6).</summary>
    public static readonly HistoryRange Year = new(
        "1 ano", "último ano, um ponto a cada 6 horas",
        TimeSpan.FromDays(365), HistoryTable.Hour, TimeSpan.FromHours(6));

    public static IReadOnlyList<HistoryRange> All { get; } = [Day, Week, Month, Year];

    /// <summary>
    /// Intervalo [From, To) a consultar. To é o fim do ponto em andamento (o minuto ou a hora atual entra),
    /// alinhado ao tamanho do ponto: enquanto o ponto atual não vira, a janela não se mexe.
    /// </summary>
    public (DateTimeOffset From, DateTimeOffset To) Window(DateTimeOffset now)
    {
        long bucket = (long)Bucket.TotalSeconds;
        var to = DateTimeOffset.FromUnixTimeSeconds((now.ToUnixTimeSeconds() / bucket + 1) * bucket);
        return (to - Span, to);
    }
}

/// <summary>
/// Um sensor num período, pronto para o gráfico: os pontos da linha e o resumo do período inteiro.
/// </summary>
/// <param name="Points">
/// Um ponto por intervalo com dados (horário = início do intervalo, valor = média) e um ponto de valor
/// nulo logo depois de cada trecho, onde não há dados — é o que faz a linha quebrar em vez de ligar
/// "antes de desligar o PC" a "depois de ligar".
/// </param>
/// <param name="Stats">Mínimo, máximo e média do período. Nulo se não há nenhum dado.</param>
public sealed record HistorySeries(IReadOnlyList<HistoryPoint> Points, SensorStats? Stats)
{
    public static HistorySeries Empty { get; } = new([], null);

    /// <summary>
    /// Monta a série a partir das linhas do banco (em ordem de horário), juntando as que caem no mesmo
    /// intervalo de tamanho <paramref name="bucket"/>. Não acessa disco: dá para testar com linhas inventadas.
    /// A média é ponderada pelo nº de leituras; mínimo e máximo são os verdadeiros (vêm das colunas
    /// mínimo/máximo do banco, não das médias).
    /// </summary>
    public static HistorySeries From(IReadOnlyList<HistoryRow> rows, TimeSpan bucket)
    {
        long size = (long)bucket.TotalSeconds;
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1, nameof(bucket));

        var points = new List<HistoryPoint>();
        double min = double.PositiveInfinity, max = double.NegativeInfinity, total = 0;
        long samples = 0;

        long? current = null;   // intervalo que está sendo acumulado
        long? previous = null;  // último intervalo que já virou ponto
        double sum = 0;         // soma (média × leituras) do intervalo atual
        long count = 0;         // leituras do intervalo atual

        foreach (var row in rows)
        {
            if (row.Samples <= 0)
                continue;

            long index = row.Start.ToUnixTimeSeconds() / size;
            if (current is { } open && open != index)
                Close(open);
            current = index;

            sum += row.Average * row.Samples;
            count += row.Samples;

            min = Math.Min(min, row.Min);
            max = Math.Max(max, row.Max);
            total += row.Average * row.Samples;
            samples += row.Samples;
        }

        if (current is { } last)
            Close(last);

        return samples == 0
            ? Empty
            : new HistorySeries(points, new SensorStats(min, max, total / samples, samples));

        void Close(long index)
        {
            // Pulou pelo menos um intervalo desde o ponto anterior: marca o buraco logo depois dele.
            if (previous is { } p && index > p + 1)
                points.Add(new HistoryPoint(At(p + 1), null));

            points.Add(new HistoryPoint(At(index), sum / count));
            previous = index;
            sum = 0;
            count = 0;
        }

        DateTimeOffset At(long index) => DateTimeOffset.FromUnixTimeSeconds(index * size);
    }
}