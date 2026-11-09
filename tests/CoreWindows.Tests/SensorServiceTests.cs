using CoreWindows.Sensors;

namespace CoreWindows.Tests;

public class SensorServiceTests
{
    /// <summary>Fonte falsa: devolve 1, 2, 3... a cada leitura. Não precisa de hardware nem admin.</summary>
    private sealed class FakeSource : ISensorSource
    {
        public int Reads;
        public bool Disposed;

        public IReadOnlyList<ComponentInfo> Components { get; } =
            [new("/fake/0", "Fake", ComponentKind.Other, null)];

        public IReadOnlyList<SensorInfo> Sensors { get; } =
            [new("/fake/0/load/0", "/fake/0", "Load", SensorKind.Load)];

        public SensorSnapshot Read()
        {
            Reads++;
            return new(DateTimeOffset.Now, new Dictionary<string, double> { ["/fake/0/load/0"] = Reads });
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

            Assert.Single(service.Sensors);
            await Task.Delay(200);
        }

        Assert.True(received >= 3, $"Esperava pelo menos 3 leituras, recebeu {received}.");
        Assert.True(fake.Disposed);
    }
}