namespace CoreWindows.Sensors;

/// <summary>Um ponto do histórico. Value nulo = o sensor não tinha valor válido naquela leitura.</summary>
public readonly record struct HistoryPoint(DateTimeOffset Timestamp, double? Value);

/// <summary>
/// Mínimo, máximo e média de um sensor desde que o app abriu (não só da janela do histórico).
/// Leituras recusadas pelo filtro do histórico (ex.: implausíveis) não entram na conta.
/// </summary>
public readonly record struct SensorStats(double Min, double Max, double Average, long Samples);

/// <summary>
/// Buffer circular na memória com as últimas leituras de todos os sensores (padrão: 30 min a 1 leitura/s).
/// Quando enche, cada leitura nova ocupa o lugar da mais antiga.
/// Pode ser usado por várias threads: o SensorService escreve, a interface lê.
/// </summary>
public sealed class SensorHistory
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(30);

    private readonly Lock _gate = new();
    private readonly DateTimeOffset[] _timestamps;
    private readonly Dictionary<string, Column> _columns = [];
    private readonly Func<string, double, bool>? _countsInStats;
    private int _next;  // posição onde a próxima leitura será gravada
    private int _count; // quantas posições já têm leitura (no máximo Capacity)

    /// <param name="capacity">Quantas leituras guardar. Ver <see cref="CapacityFor"/>.</param>
    /// <param name="countsInStats">
    /// Recebe (id do sensor, valor); false = o valor fica no histórico, mas fora das estatísticas.
    /// Sem filtro, todo valor válido conta.
    /// </param>
    public SensorHistory(int capacity, Func<string, double, bool>? countsInStats = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
        _countsInStats = countsInStats;
        _timestamps = new DateTimeOffset[capacity];
    }

    /// <summary>Quantas leituras cabem numa janela de tempo, dado o intervalo entre elas.</summary>
    public static int CapacityFor(TimeSpan window, TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        return Math.Max(1, (int)Math.Ceiling(window / interval));
    }

    public int Capacity { get; }

    /// <summary>Quantas leituras estão guardadas agora.</summary>
    public int Count
    {
        get { lock (_gate) return _count; }
    }

    public void Add(SensorSnapshot snapshot)
    {
        lock (_gate)
        {
            int slot = _next;
            _timestamps[slot] = snapshot.Timestamp;

            // A posição pode conter um valor de 30 min atrás: limpa todas as colunas antes de gravar,
            // para um sensor que ficou sem valor nesta leitura não "herdar" o valor antigo.
            foreach (var column in _columns.Values)
                column.Values[slot] = double.NaN;

            foreach (var (id, value) in snapshot.Values)
            {
                if (!double.IsFinite(value))
                    continue;

                if (!_columns.TryGetValue(id, out var column))
                    _columns[id] = column = new Column(Capacity);

                column.Values[slot] = value;

                // A leitura fica guardada de qualquer forma (o gráfico mostra); só as estatísticas a ignoram.
                if (_countsInStats is not null && !_countsInStats(id, value))
                    continue;

                column.Min = Math.Min(column.Min, value);
                column.Max = Math.Max(column.Max, value);
                column.Sum += value;
                column.Samples++;
            }

            _next = (slot + 1) % Capacity;
            if (_count < Capacity)
                _count++;
        }
    }

    /// <summary>
    /// Cópia das leituras guardadas de um sensor, da mais antiga para a mais recente.
    /// Sempre tem um ponto por leitura guardada; onde o sensor não tinha valor, Value é nulo.
    /// Sensor desconhecido → lista vazia.
    /// </summary>
    public IReadOnlyList<HistoryPoint> GetSeries(string sensorId)
    {
        lock (_gate)
        {
            if (!_columns.TryGetValue(sensorId, out var column))
                return [];

            var points = new HistoryPoint[_count];
            int oldest = (_next - _count + Capacity) % Capacity;

            for (int i = 0; i < _count; i++)
            {
                int slot = (oldest + i) % Capacity;
                double value = column.Values[slot];
                points[i] = new HistoryPoint(_timestamps[slot], double.IsNaN(value) ? null : value);
            }
            return points;
        }
    }

    /// <summary>Estatísticas da sessão inteira. Nulo se o sensor nunca teve um valor que conte.</summary>
    public SensorStats? GetStats(string sensorId)
    {
        lock (_gate)
        {
            if (!_columns.TryGetValue(sensorId, out var column) || column.Samples == 0)
                return null;

            return new SensorStats(column.Min, column.Max, column.Sum / column.Samples, column.Samples);
        }
    }

    /// <summary>Os valores de um sensor (NaN = sem valor) e as estatísticas acumuladas dele.</summary>
    private sealed class Column
    {
        public readonly double[] Values;
        public double Min = double.PositiveInfinity;
        public double Max = double.NegativeInfinity;
        public double Sum;
        public long Samples;

        public Column(int capacity)
        {
            Values = new double[capacity];
            Array.Fill(Values, double.NaN);
        }
    }
}
