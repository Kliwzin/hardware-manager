using CoreWindows.Sensors;

namespace CoreWindows.Tests;

public class SensorServiceTests
{
    /// <summary>
    /// Fonte falsa: a carga devolve 1, 2, 3... a cada leitura; a temperatura fica sempre em -14 °C
    /// (como uma entrada da placa-mãe sem nada ligado). Não precisa de hardware nem admin.
    /// </summary>
    private sealed class FakeSource : ISensorSource
    {
        public int Reads;
        public bool Disposed;

        public IReadOnlyList<ComponentInfo> Components { get; } =
            [new("/fake/0", "Fake", ComponentKind.Other, null)];

        public IReadOnlyList<SensorInfo> Sensors { get; } =
        [
            new("/fake/0/load/0", "/fake/0", "Load", SensorKind.Load),
            new("/fake/0/temperature/0", "/fake/0", "Temperature", SensorKind.Temperature),
            new("/fake/0/temperature/0", "/fake/0", "Repetido", SensorKind.Temperature), // id repetido não pode derrubar o serviço
        ];

        public SensorSnapshot Read()
        {
            Reads++;
            return new(DateTimeOffset.Now, new Dictionary<string, double>
            {
                ["/fake/0/load/0"] = Reads,
                ["/fake/0/temperature/0"] = -14,
            });
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task Publica_leituras_periodicas_e_libera_a_fonte_ao_final()
    {
        var fake = new FakeSource();
        int received = 0;

        await using (var service = new SensorService(() => fake, TimeSpan.FromMilliseconds(20)))
        {
            service.SnapshotTaken += (_, _) => Interlocked.Increment(ref received);
            service.Start();
            await service.Ready;

            Assert.Equal(3, service.Sensors.Count);
            await Task.Delay(200);
        }

        Assert.True(received >= 3, $"Esperava pelo menos 3 leituras, recebeu {received}.");
        Assert.True(fake.Disposed);
    }

    [Fact]
    public async Task Historico_ja_contem_a_leitura_quando_o_evento_dispara()
    {
        const string id = "/fake/0/load/0";
        int events = 0, outOfSync = 0;

        await using var service = new SensorService(
            () => new FakeSource(), TimeSpan.FromMilliseconds(20), historyWindow: TimeSpan.FromSeconds(1));

        service.SnapshotTaken += (_, snapshot) =>
        {
            Interlocked.Increment(ref events);
            var last = service.History.GetSeries(id)[^1];
            if (last.Timestamp != snapshot.Timestamp || last.Value != snapshot.Values[id])
                Interlocked.Increment(ref outOfSync);
        };
        service.Start();
        await service.Ready;
        await Task.Delay(200);

        Assert.Equal(50, service.History.Capacity); // 1 s de janela ÷ 20 ms de intervalo
        Assert.True(events >= 3, $"Esperava pelo menos 3 leituras, recebeu {events}.");
        Assert.Equal(0, outOfSync);
        Assert.Equal(1.0, service.History.GetStats(id)!.Value.Min); // a 1ª leitura (antes de Ready) também entrou
    }

    /// <summary>Fonte com defeito: dois sensores com o mesmo id (como o LHM faz na GPU NVIDIA).</summary>
    private sealed class DuplicateIdSource : ISensorSource
    {
        public IReadOnlyList<ComponentInfo> Components { get; } =
            [new("/fake/0", "Fake", ComponentKind.Gpu, null)];

        public IReadOnlyList<SensorInfo> Sensors { get; } =
        [
            new("/fake/0/load/3", "/fake/0", "GPU Bus", SensorKind.Load),
            new("/fake/0/load/3", "/fake/0", "GPU Memory", SensorKind.Load),
        ];

        public SensorSnapshot Read()
            => new(DateTimeOffset.Now, new Dictionary<string, double> { ["/fake/0/load/3"] = 5 });

        public void Dispose() { }
    }

    [Fact]
    public async Task Fonte_com_ids_repetidos_nao_derruba_o_servico()
    {
        await using var service = new SensorService(() => new DuplicateIdSource(), TimeSpan.FromMilliseconds(20));
        service.Start();

        await service.Ready; // antes da correção, lançava "An item with the same key has already been added"

        Assert.NotNull(service.Latest);
        Assert.Equal(5.0, service.History.GetSeries("/fake/0/load/3")[^1].Value);
    }

    [Fact]
    public async Task Leitura_implausivel_fica_no_historico_mas_fora_das_estatisticas()
    {
        const string id = "/fake/0/temperature/0";

        await using var service = new SensorService(() => new FakeSource(), TimeSpan.FromMilliseconds(20));
        service.Start();
        await service.Ready;

        Assert.Equal(-14.0, service.History.GetSeries(id)[^1].Value);
        Assert.Null(service.History.GetStats(id));
    }
}