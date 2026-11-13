using CoreWindows.Sensors;
using CoreWindows.Storage;
using Microsoft.Data.Sqlite;

namespace CoreWindows.Tests;

public sealed class HistoryRecorderTests : IDisposable
{
    private const string Load = "/fake/0/load/0";
    private const string Temperature = "/fake/0/temperature/0";

    private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hm-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    /// <summary>
    /// Fonte falsa com relógio próprio: cada leitura "acontece" 20 s depois da anterior,
    /// então um minuto fecha a cada 3 leituras sem o teste precisar esperar.
    /// A carga é sempre 10 %; a temperatura é sempre -14 °C (implausível).
    /// </summary>
    private sealed class FastClockSource : ISensorSource
    {
        private int _reads;

        public IReadOnlyList<ComponentInfo> Components { get; } =
            [new("/fake/0", "Fake", ComponentKind.Other, null)];

        public IReadOnlyList<SensorInfo> Sensors { get; } =
        [
            new(Load, "/fake/0", "Load", SensorKind.Load),
            new(Temperature, "/fake/0", "Temperature", SensorKind.Temperature),
        ];

        public SensorSnapshot Read()
        {
            int read = _reads++;
            return new(Start.AddSeconds(20 * read), new Dictionary<string, double>
            {
                [Load] = 10,
                [Temperature] = -14,
            });
        }

        public void Dispose() { }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20);
        Assert.True(condition(), "A condição não aconteceu em 5 s.");
    }

    [Fact]
    public async Task Grava_um_resumo_por_minuto_sem_as_leituras_implausiveis()
    {
        var store = new HistoryStore(Path.Combine(_folder, "history.db"));

        await using (var service = new SensorService(() => new FastClockSource(), TimeSpan.FromMilliseconds(10)))
        using (var recorder = new HistoryRecorder(service, store))
        {
            recorder.Start();
            service.Start();
            await WaitUntilAsync(() => recorder.MinutesWritten >= 3);

            Assert.Null(recorder.LastError);
        }

        var rows = store.ReadMinutes(Load, Start, Start.AddHours(1));
        Assert.True(rows.Count >= 3);
        Assert.Equal(new HistoryRow(Start, Average: 10, Min: 10, Max: 10, Samples: 3), rows[0]);
        Assert.Equal(Start.AddMinutes(1), rows[1].Start);

        Assert.Empty(store.ReadMinutes(Temperature, Start, Start.AddHours(1)));
        Assert.Equal(2, store.GetInfo().Sensors); // o sensor fica registrado mesmo sem linhas
    }

    [Fact]
    public async Task Ao_encerrar_grava_o_minuto_em_andamento()
    {
        var store = new HistoryStore(Path.Combine(_folder, "history.db"));
        var service = new SensorService(() => new FastClockSource(), TimeSpan.FromSeconds(30));
        var recorder = new HistoryRecorder(service, store);

        recorder.Start();
        service.Start();
        await service.Ready; // só a primeira leitura (a próxima viria em 30 s)

        Assert.Equal(0, recorder.MinutesWritten);
        recorder.Dispose();
        await service.DisposeAsync();

        var row = Assert.Single(store.ReadMinutes(Load, Start, Start.AddHours(1)));
        Assert.Equal(1, row.Samples);
    }

    [Fact]
    public async Task Falha_de_gravacao_nao_derruba_o_servico_e_avisa_uma_vez()
    {
        // Caminho impossível: a "pasta" do banco é, na verdade, um arquivo.
        Directory.CreateDirectory(_folder);
        string blocker = Path.Combine(_folder, "arquivo.txt");
        File.WriteAllText(blocker, "x");
        var store = new HistoryStore(Path.Combine(blocker, "history.db"));

        int failures = 0, snapshots = 0;
        await using var service = new SensorService(() => new FastClockSource(), TimeSpan.FromMilliseconds(10));
        using var recorder = new HistoryRecorder(service, store);
        recorder.Failed += _ => Interlocked.Increment(ref failures);
        service.SnapshotTaken += (_, _) => Interlocked.Increment(ref snapshots);

        recorder.Start();
        service.Start();
        await WaitUntilAsync(() => snapshots >= 10);

        Assert.NotNull(recorder.LastError);
        Assert.Equal(1, failures);
        Assert.Null(service.LastError); // o laço de leitura nem ficou sabendo
    }
}