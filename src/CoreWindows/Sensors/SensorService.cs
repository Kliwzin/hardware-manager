namespace CoreWindows.Sensors;

/// <summary>
/// Lê a fonte de sensores em intervalo fixo, numa tarefa em segundo plano,
/// e guarda as leituras recentes em <see cref="History"/>.
/// Uma leitura que falha não para o serviço: ele avisa por <see cref="ReadFailed"/> e tenta de novo no ciclo seguinte.
/// ATENÇÃO: SnapshotTaken e ReadFailed são disparados fora da thread da interface.
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

    /// <summary>Erro do último ciclo de leitura; nulo se ele deu certo.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Disparado quando um ciclo falha depois de outro ter dado certo (uma vez por sequência de falhas,
    /// não a cada segundo). A falha pode vir da leitura do hardware ou de quem assina SnapshotTaken.
    /// </summary>
    public event EventHandler<Exception>? ReadFailed;

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
                ReadOnce(_source);
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

    /// <summary>Um ciclo do laço. Nenhuma exceção sai daqui: antes, a primeira falha encerrava o laço em silêncio.</summary>
    private void ReadOnce(ISensorSource source)
    {
        try
        {
            Publish(source.Read());
            LastError = null;
        }
        catch (Exception ex)
        {
            bool first = LastError is null;
            LastError = ex;
            if (first)
                ReadFailed?.Invoke(this, ex);
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