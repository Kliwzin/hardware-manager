namespace CoreWindows.Sensors;

/// <summary>
/// Lê a fonte de sensores em intervalo fixo, numa tarefa em segundo plano.
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

    public SensorService(Func<ISensorSource> sourceFactory, TimeSpan? interval = null)
    {
        _sourceFactory = sourceFactory;
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    public IReadOnlyList<ComponentInfo> Components => _source?.Components ?? [];
    public IReadOnlyList<SensorInfo> Sensors => _source?.Sensors ?? [];
    public SensorSnapshot? Latest { get; private set; }

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

    private void Publish(SensorSnapshot snapshot)
    {
        Latest = snapshot;
        SnapshotTaken?.Invoke(this, snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null || _loop is null)
            return;

        await _cts.CancelAsync();
        try { await _loop; }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}