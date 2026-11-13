using CoreWindows.Sensors;
using CoreWindows.Storage;
using Microsoft.Data.Sqlite;

namespace CoreWindows.Tests;

/// <summary>Cada teste usa um arquivo de banco próprio, numa pasta temporária apagada ao final.</summary>
public sealed class HistoryStoreTests : IDisposable
{
    private const string Cpu = "/intelcpu/0/temperature/6";
    private const string Gpu = "/gpu-nvidia/0/temperature/0";

    private static readonly SensorInfo[] Sensors =
    [
        new(Cpu, "/intelcpu/0", "CPU Package", SensorKind.Temperature),
        new(Gpu, "/gpu-nvidia/0", "GPU Core", SensorKind.Temperature),
    ];

    // 12:00 UTC: começo exato de uma hora.
    private static readonly DateTimeOffset Noon = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hm-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public HistoryStoreTests() => _path = Path.Combine(_folder, "sub", "history.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // sem isso, o Windows não deixa apagar o arquivo
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private HistoryStore NewStore(TimeSpan? minuteRetention = null, TimeSpan? hourRetention = null)
    {
        var store = new HistoryStore(_path, minuteRetention, hourRetention);
        store.RegisterSensors(Sensors);
        return store;
    }

    /// <summary>Um minuto com um sensor só, "minutes" minutos depois do meio-dia.</summary>
    private static MinuteBatch Batch(int minutes, string id, double average, double min, double max, int samples = 60)
        => new(MinuteAggregator.MinuteOf(Noon.AddMinutes(minutes)), [new SensorAggregate(id, average, min, max, samples)]);

    [Fact]
    public void Construtor_nao_cria_o_arquivo_o_primeiro_uso_sim()
    {
        var store = new HistoryStore(_path);
        Assert.False(File.Exists(_path));

        store.RegisterSensors(Sensors);

        Assert.True(File.Exists(_path)); // criou também a pasta "sub"
        Assert.Equal(2, store.GetInfo().Sensors);
    }

    [Fact]
    public void Grava_e_le_minutos_em_ordem_dentro_do_intervalo()
    {
        var store = NewStore();
        store.Write(Batch(2, Cpu, 42, 40, 44));
        store.Write(Batch(0, Cpu, 30, 29, 31));
        store.Write(Batch(1, Cpu, 35, 33, 37));
        store.Write(Batch(1, Gpu, 50, 50, 50));

        var rows = store.ReadMinutes(Cpu, Noon, Noon.AddMinutes(2)); // [12:00, 12:02): o minuto 2 fica de fora

        Assert.Equal(2, rows.Count);
        Assert.Equal(new HistoryRow(Noon, 30, 29, 31, 60), rows[0]);
        Assert.Equal(new HistoryRow(Noon.AddMinutes(1), 35, 33, 37, 60), rows[1]);
        Assert.Empty(store.ReadMinutes("/nao/existe", Noon, Noon.AddHours(1)));
    }

    [Fact]
    public void Mesmo_minuto_gravado_em_duas_partes_vira_uma_linha_so()
    {
        // Caso real: fechar e reabrir o app dentro do mesmo minuto.
        var store = NewStore();
        store.Write(Batch(0, Cpu, average: 30, min: 28, max: 32, samples: 20));
        store.Write(Batch(0, Cpu, average: 60, min: 55, max: 65, samples: 10));

        var row = Assert.Single(store.ReadMinutes(Cpu, Noon, Noon.AddMinutes(1)));

        Assert.Equal(new HistoryRow(Noon, Average: 40, Min: 28, Max: 65, Samples: 30), row); // (30×20 + 60×10) ÷ 30
    }

    [Fact]
    public void A_hora_acumula_os_minutos_dela()
    {
        var store = NewStore();
        store.Write(Batch(0, Cpu, 30, 29, 31));
        store.Write(Batch(59, Cpu, 50, 45, 58));
        store.Write(Batch(60, Cpu, 70, 70, 70)); // já é a hora seguinte

        var hours = store.ReadHours(Cpu, Noon, Noon.AddHours(2));

        Assert.Equal(2, hours.Count);
        Assert.Equal(new HistoryRow(Noon, Average: 40, Min: 29, Max: 58, Samples: 120), hours[0]);
        Assert.Equal(new HistoryRow(Noon.AddHours(1), 70, 70, 70, 60), hours[1]);
    }

    [Fact]
    public void Sensor_nao_registrado_e_ignorado()
    {
        var store = NewStore();

        int written = store.Write(new MinuteBatch(MinuteAggregator.MinuteOf(Noon),
        [
            new SensorAggregate(Cpu, 30, 30, 30, 60),
            new SensorAggregate("/desconhecido/0", 1, 1, 1, 60),
        ]));

        Assert.Equal(1, written);
        Assert.Equal(1, store.GetInfo().MinuteRows);
    }

    [Fact]
    public void Limpeza_apaga_minutos_antigos_e_mantem_as_horas()
    {
        var store = NewStore(minuteRetention: TimeSpan.FromHours(1), hourRetention: TimeSpan.FromDays(1));
        store.Write(Batch(0, Cpu, 30, 30, 30));    // 12:00
        store.Write(Batch(90, Cpu, 40, 40, 40));   // 13:30

        int deleted = store.Prune(now: Noon.AddHours(2)); // 14:00 → minutos antes das 13:00 saem

        Assert.Equal(1, deleted);
        Assert.Equal(Noon.AddMinutes(90), Assert.Single(store.ReadMinutes(Cpu, Noon, Noon.AddHours(3))).Start);
        Assert.Equal(2, store.ReadHours(Cpu, Noon, Noon.AddHours(3)).Count);

        Assert.Equal(3, store.Prune(now: Noon.AddDays(3))); // três dias depois vence tudo: 1 minuto + 2 horas
        Assert.Equal(0, store.GetInfo().HourRows);
    }

    [Fact]
    public void Reabrir_o_arquivo_mantem_dados_e_sensores()
    {
        NewStore().Write(Batch(0, Cpu, 30, 29, 31));

        // Outro objeto, mesmo arquivo: como abrir o app de novo.
        var reopened = new HistoryStore(_path);

        Assert.Equal(new HistoryRow(Noon, 30, 29, 31, 60), Assert.Single(reopened.ReadMinutes(Cpu, Noon, Noon.AddHours(1))));
        Assert.Equal(1, reopened.Write(Batch(1, Cpu, 31, 31, 31))); // os ids dos sensores foram recarregados

        var sensors = reopened.ReadSensors();
        Assert.Equal(2, sensors.Count);
        Assert.Contains(new SensorInfo(Cpu, "/intelcpu/0", "CPU Package", SensorKind.Temperature), sensors);
    }

    [Fact]
    public void Info_resume_o_banco()
    {
        var store = NewStore();
        Assert.Null(store.GetInfo().FirstMinute);

        store.Write(Batch(5, Cpu, 30, 30, 30));
        store.Write(Batch(65, Gpu, 40, 40, 40));

        var info = store.GetInfo();

        Assert.Equal(2, info.MinuteRows);
        Assert.Equal(2, info.HourRows);
        Assert.Equal(Noon.AddMinutes(5), info.FirstMinute);
        Assert.Equal(Noon.AddMinutes(65), info.LastMinute);
        Assert.True(info.FileBytes > 0);
    }
}