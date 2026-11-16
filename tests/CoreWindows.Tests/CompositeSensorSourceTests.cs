using CoreWindows.Sensors;

namespace CoreWindows.Tests;

public class CompositeSensorSourceTests
{
    private static readonly DateTimeOffset PrimaryTime = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Fonte falsa com sensores e valores fixos; pode falhar ao ler ou ao fechar.</summary>
    private sealed class StubSource(DateTimeOffset time, params (string Id, double Value)[] sensors) : ISensorSource
    {
        public bool FailOnRead;
        public bool FailOnDispose;
        public bool Disposed;

        public IReadOnlyList<ComponentInfo> Components { get; init; } =
            [new("/gpu/0", "GPU", ComponentKind.Gpu, null)];

        public IReadOnlyList<SensorInfo> Sensors { get; } =
            [.. sensors.Select(s => new SensorInfo(s.Id, "/gpu/0", s.Id, SensorKind.Power))];

        public SensorSnapshot Read()
        {
            if (FailOnRead)
                throw new InvalidOperationException("falha simulada");
            return new(time, sensors.ToDictionary(s => s.Id, s => s.Value));
        }

        public void Dispose()
        {
            Disposed = true;
            if (FailOnDispose)
                throw new InvalidOperationException("falha simulada ao fechar");
        }
    }

    [Fact]
    public void Junta_sensores_e_valores_com_o_horario_da_fonte_principal()
    {
        var primary = new StubSource(PrimaryTime, ("/gpu/0/temperature/0", 60));
        var extra = new StubSource(PrimaryTime.AddSeconds(5), ("/gpu/0/nvml/power", 115));
        using var source = new CompositeSensorSource(primary, extra);

        var snapshot = source.Read();

        Assert.Equal(new[] { "/gpu/0/temperature/0", "/gpu/0/nvml/power" }, source.Sensors.Select(s => s.Id));
        Assert.Single(source.Components); // o componente repetido da fonte extra não entra duas vezes
        Assert.Equal(PrimaryTime, snapshot.Timestamp);
        Assert.Equal(60, snapshot.Values["/gpu/0/temperature/0"]);
        Assert.Equal(115, snapshot.Values["/gpu/0/nvml/power"]);
    }

    [Fact]
    public void Fonte_extra_acrescenta_componentes_novos()
    {
        var primary = new StubSource(PrimaryTime);
        var extra = new StubSource(PrimaryTime)
        {
            Components = [new("/outro/0", "Outro", ComponentKind.Other, null)],
        };
        using var source = new CompositeSensorSource(primary, extra);

        Assert.Equal(new[] { "/gpu/0", "/outro/0" }, source.Components.Select(c => c.Id));
    }

    [Fact]
    public void Fonte_extra_nunca_substitui_um_sensor_da_principal()
    {
        var primary = new StubSource(PrimaryTime, ("/gpu/0/power/0", 100));
        var extra = new StubSource(PrimaryTime, ("/gpu/0/power/0", 999), ("/gpu/0/nvml/power", 115));
        using var source = new CompositeSensorSource(primary, extra);

        var snapshot = source.Read();

        Assert.Equal(2, source.Sensors.Count);
        Assert.Equal(100, snapshot.Values["/gpu/0/power/0"]);
    }

    [Fact]
    public void Falha_da_fonte_extra_nao_derruba_a_leitura()
    {
        var primary = new StubSource(PrimaryTime, ("/gpu/0/temperature/0", 60));
        var extra = new StubSource(PrimaryTime, ("/gpu/0/nvml/power", 115)) { FailOnRead = true };
        using var source = new CompositeSensorSource(primary, extra);

        var snapshot = source.Read();

        Assert.Equal(60, snapshot.Values["/gpu/0/temperature/0"]);
        Assert.False(snapshot.Values.ContainsKey("/gpu/0/nvml/power"));
        Assert.NotNull(source.LastExtraError);

        extra.FailOnRead = false;
        Assert.Equal(115, source.Read().Values["/gpu/0/nvml/power"]);
        Assert.Null(source.LastExtraError);
    }

    [Fact]
    public void Falha_da_fonte_principal_continua_sendo_uma_falha()
    {
        var primary = new StubSource(PrimaryTime) { FailOnRead = true };
        using var source = new CompositeSensorSource(primary, new StubSource(PrimaryTime));

        Assert.Throws<InvalidOperationException>(() => source.Read());
    }

    [Fact]
    public void Dispose_fecha_todas_as_fontes_mesmo_se_uma_falhar()
    {
        var primary = new StubSource(PrimaryTime);
        var extra = new StubSource(PrimaryTime) { FailOnDispose = true };

        new CompositeSensorSource(primary, extra).Dispose();

        Assert.True(extra.Disposed);
        Assert.True(primary.Disposed);
    }
}