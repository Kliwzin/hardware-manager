using CoreWindows.Sensors;

namespace CoreWindows.Storage;

/// <summary>Resumo de um sensor num período: média, mínimo, máximo e quantas leituras entraram na conta.</summary>
public readonly record struct SensorAggregate(string SensorId, double Average, double Min, double Max, int Samples);

/// <summary>Os resumos de todos os sensores num minuto. Minute = minutos desde 1970 (UTC).</summary>
public sealed record MinuteBatch(long Minute, IReadOnlyList<SensorAggregate> Sensors)
{
    public DateTimeOffset Start => DateTimeOffset.FromUnixTimeSeconds(Minute * 60);
}

/// <summary>
/// Junta as leituras de 1 s em resumos por minuto. Não acessa disco nem relógio: o minuto vem do
/// horário de cada leitura, então dá para testar sem esperar o tempo passar.
/// Não é seguro entre threads; quem usa (HistoryRecorder) cuida disso.
/// </summary>
/// <param name="accepts">
/// Recebe (id do sensor, valor); false = a leitura fica fora do resumo (ex.: implausível).
/// Sem filtro, todo valor válido entra.
/// </param>
public sealed class MinuteAggregator(Func<string, double, bool>? accepts = null)
{
    private readonly Dictionary<string, Bucket> _buckets = [];
    private long? _minute; // minuto que está sendo acumulado agora

    public static long MinuteOf(DateTimeOffset time) => time.ToUnixTimeSeconds() / 60;

    /// <summary>
    /// Acrescenta uma leitura. Quando ela pertence a outro minuto, devolve o minuto anterior, já fechado;
    /// caso contrário devolve nulo.
    /// </summary>
    public MinuteBatch? Add(SensorSnapshot snapshot)
    {
        long minute = MinuteOf(snapshot.Timestamp);

        // "Diferente", e não "maior": se o relógio do Windows voltar atrás, o minuto atual também é fechado.
        MinuteBatch? completed = _minute is { } current && current != minute ? Close() : null;
        _minute = minute;

        foreach (var (id, value) in snapshot.Values)
        {
            if (!double.IsFinite(value) || (accepts is not null && !accepts(id, value)))
                continue;

            if (!_buckets.TryGetValue(id, out var bucket))
                _buckets[id] = bucket = new Bucket();

            bucket.Min = Math.Min(bucket.Min, value);
            bucket.Max = Math.Max(bucket.Max, value);
            bucket.Sum += value;
            bucket.Samples++;
        }
        return completed;
    }

    /// <summary>Fecha o minuto em andamento, mesmo incompleto (usado ao encerrar o app). Nulo se não há nada.</summary>
    public MinuteBatch? Flush() => _minute is null ? null : Close();

    private MinuteBatch? Close()
    {
        long minute = _minute!.Value;
        _minute = null;

        var sensors = _buckets
            .Select(b => new SensorAggregate(b.Key, b.Value.Sum / b.Value.Samples, b.Value.Min, b.Value.Max, b.Value.Samples))
            .ToList();
        _buckets.Clear();

        return sensors.Count == 0 ? null : new MinuteBatch(minute, sensors);
    }

    private sealed class Bucket
    {
        public double Min = double.PositiveInfinity;
        public double Max = double.NegativeInfinity;
        public double Sum;
        public int Samples;
    }
}