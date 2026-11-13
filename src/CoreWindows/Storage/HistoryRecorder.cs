using CoreWindows.Sensors;

namespace CoreWindows.Storage;

/// <summary>
/// Liga o SensorService ao banco: acompanha as leituras de 1 s, fecha um resumo por minuto e grava no
/// <see cref="HistoryStore"/>. Roda na thread do SensorService (o evento SnapshotTaken).
/// Nenhuma falha de disco ou de banco sai daqui: o app continua mostrando os sensores, só sem gravar.
/// </summary>
public sealed class HistoryRecorder : IDisposable
{
    private readonly Lock _gate = new();
    private readonly SensorService _sensors;
    private readonly HistoryStore _store;
    private MinuteAggregator? _aggregator; // criado na primeira leitura, quando a lista de sensores já existe
    private bool _started;
    private bool _disposed;

    public HistoryRecorder(SensorService sensors, HistoryStore store)
    {
        _sensors = sensors;
        _store = store;
    }

    /// <summary>Último erro de gravação; nulo se a última gravação deu certo (ou ainda não houve nenhuma).</summary>
    public Exception? LastError { get; private set; }

    /// <summary>Quantos minutos já foram gravados nesta sessão.</summary>
    public int MinutesWritten { get; private set; }

    /// <summary>
    /// Disparado quando uma gravação falha depois de outra ter dado certo (uma vez por sequência de falhas).
    /// ATENÇÃO: fora da thread da interface.
    /// </summary>
    public event Action<Exception>? Failed;

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
                return;
            _started = true;
        }
        _sensors.SnapshotTaken += OnSnapshot;
    }

    private void OnSnapshot(object? sender, SensorSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            Guarded(() =>
            {
                if (_aggregator is null)
                {
                    var sensors = _sensors.Sensors;
                    _store.RegisterSensors(sensors);
                    _store.Prune(snapshot.Timestamp);
                    _aggregator = new MinuteAggregator(PlausibilityFilter(sensors));
                }

                if (_aggregator.Add(snapshot) is { } batch)
                {
                    _store.Write(batch);
                    MinutesWritten++;

                    // Limpeza do que venceu: uma vez por hora, logo depois de fechar o último minuto dela.
                    if (batch.Minute % 60 == 59)
                        _store.Prune(snapshot.Timestamp);
                }
            });
        }
    }

    public void Dispose()
    {
        _sensors.SnapshotTaken -= OnSnapshot;

        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;

            // Grava o minuto que estava em andamento. Se o app reabrir no mesmo minuto,
            // o banco junta as duas partes numa linha só.
            Guarded(() =>
            {
                if (_aggregator?.Flush() is { } batch)
                {
                    _store.Write(batch);
                    MinutesWritten++;
                }
            });
        }
    }

    private void Guarded(Action action)
    {
        try
        {
            action();
            LastError = null;
        }
        catch (Exception ex)
        {
            bool first = LastError is null;
            LastError = ex;
            if (first)
                Failed?.Invoke(ex);
        }
    }

    /// <summary>Mesma regra das estatísticas da sessão: leitura implausível não entra nos resumos.</summary>
    private static Func<string, double, bool> PlausibilityFilter(IReadOnlyList<SensorInfo> sensors)
    {
        // TryAdd, e não ToDictionary: ids vindos de fora podem se repetir.
        var kinds = new Dictionary<string, SensorKind>();
        foreach (var sensor in sensors)
            kinds.TryAdd(sensor.Id, sensor.Kind);

        return (id, value) => !kinds.TryGetValue(id, out var kind) || SensorPlausibility.IsPlausible(kind, value);
    }
}