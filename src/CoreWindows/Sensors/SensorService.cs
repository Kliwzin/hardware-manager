namespace CoreWindows.Sensors;

/// <summary>
/// Lê a fonte de sensores em intervalo fixo, numa tarefa em segundo plano,
/// e guarda as leituras recentes em <see cref="History"/>.
/// ATENÇÃO: SnapshotTaken é disparado fora da thread da interface.
/// </summary>
public sealed class SensorService : IAsyncDisposable
{
    private readonly Func<ISensorSource> _sourceFactory;
    private readonly TimeSpan _interval;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private ISensorSource? _source;
    private Dictionary<string, SensorKind>? _kinds; // id do sensor → tipo; preenchido quando a fonte abre

    /// <param name="interval">Intervalo entre leituras (padrão: 1 s).</param>
    /// <param name="historyWindow">Quanto tempo de leituras fica na memória (padrão: 30 min).</param>
    public SensorService(Func<ISensorSource> sourceFactory, TimeSpan? interval = null, TimeSpan? historyWindow = null)
    {
        _sourceFactory = sourceFactory;
        _interval = interval ?? TimeSpan.FromSeconds(1);
        History = new SensorHistory(
            SensorHistory.CapacityFor(historyWindow ?? SensorHistory.DefaultWindow, _interval),
            IsPlausible);
    }

    public IReadOnlyList<ComponentInfo> Components => _source?.Components ?? [];
    public IReadOnlyList<SensorInfo> Sensors => _source?.Sensors ?? [];
    public SensorSnapshot? Latest { get; private set; }

    /// <summary>
    /// Leituras recentes de todos os sensores + mínimo/máximo/média da sessão.
    /// Leituras implausíveis (ver <see cref="SensorPlausibility"/>) ficam no histórico, mas fora das estatísticas.
    /// </summary>
    public SensorHistory History { get; }

    /// <summary>Completa quando a fonte foi aberta e a primeira leitura foi feita.</summary>
    public Task Ready => _ready.Task;

    public event EventHandler<SensorSnapshot>? SnapshotTaken;

    public void Start()
    {
        if (_loop is not null)
            throw new InvalidOperationException("O serviço já foi iniciado.");

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            // Abrir o LHM demora 1–2 s: acontece aqui, fora da thread da interface.
            _source = _sourceFactory();

            // TryAdd, e não ToDictionary: uma fonte com ids repetidos não pode derrubar o serviço.
            _kinds = [];
            foreach (var sensor in _source.Sensors)
                _kinds.TryAdd(sensor.Id, sensor.Kind);

            Publish(_source.Read());
            _ready.TrySetResult();

            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(ct))
                Publish(_source.Read());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Parada normal.
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
            throw;
        }
        finally
        {
            _source?.Dispose();
        }
    }

    private bool IsPlausible(string sensorId, double value)
        => _kinds is null
        || !_kinds.TryGetValue(sensorId, out var kind)
        || SensorPlausibility.IsPlausible(kind, value);

    private void Publish(SensorSnapshot snapshot)
    {
        // Primeiro o histórico: quem recebe o evento já encontra esta leitura lá dentro.
        History.Add(snapshot);
        Latest = snapshot;
        SnapshotTaken?.Invoke(this, snapshot);
    }

    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed || _cts is null || _loop is null)
            return;
        _disposed = true;

        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch
        {
            // Parada normal (cancelamento) ou erro já informado por Ready: nada a fazer ao encerrar.
        }
        _cts.Dispose();
    }
}