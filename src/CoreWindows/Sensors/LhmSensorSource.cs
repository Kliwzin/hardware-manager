using System.Collections.Concurrent;
using System.Diagnostics;
using CoreWindows.Platform;
using LibreHardwareMonitor.Hardware;

namespace CoreWindows.Sensors;

/// <summary>
/// Única classe do projeto que conhece o LibreHardwareMonitor.
/// Atualiza em dois ritmos: componentes rápidos em Read() (chamado pelo SensorService a cada 1 s)
/// e armazenamento num laço próprio, porque o Update() de discos pode levar segundos.
/// </summary>
public sealed class LhmSensorSource : ISensorSource
{
    public static readonly TimeSpan DefaultSlowInterval = TimeSpan.FromSeconds(5);

    private readonly Computer _computer;
    private readonly List<(IHardware Hardware, string Id)> _fastHardware = [];
    private readonly List<(IHardware Hardware, string Id)> _slowHardware = [];
    private readonly List<(ISensor Sensor, SensorInfo Info)> _sensors = [];
    private readonly List<ComponentInfo> _components = [];
    private readonly List<SensorInfo> _sensorInfos = [];
    private readonly ConcurrentDictionary<string, TimeSpan> _updateDurations = new();
    private readonly CancellationTokenSource _slowCts = new();
    private readonly Task _slowLoop;

    public IReadOnlyList<ComponentInfo> Components => _components;
    public IReadOnlyList<SensorInfo> Sensors => _sensorInfos;

    /// <summary>Quanto tempo o Update() de cada componente levou na última vez.</summary>
    public IReadOnlyDictionary<string, TimeSpan> LastUpdateDurations => _updateDurations;

    /// <summary>Fonte com os filtros padrão (só adaptadores de rede físicos).</summary>
    public static LhmSensorSource CreateDefault()
    {
        var physical = PhysicalNetworkAdapters.GetGuids();
        return new LhmSensorSource(id =>
            PhysicalNetworkAdapters.GuidFromIdentifier(id) is not { } guid || physical.Contains(guid));
    }

    /// <param name="includeComponent">Recebe o id do componente; false = ignorar (e seus filhos).</param>
    /// <param name="slowInterval">Intervalo do laço de armazenamento (padrão: 5 s).</param>
    public LhmSensorSource(Func<string, bool> includeComponent, TimeSpan? slowInterval = null)
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
        };
        _computer.Open();

        // Alguns sensores só passam a existir depois da primeira atualização.
        foreach (var hw in _computer.Hardware)
            UpdateTree(hw);

        bool pawnIo = DriverStatus.IsPawnIoInstalled;
        foreach (var hw in _computer.Hardware)
            Register(hw, parentId: null, includeComponent, pawnIo);

        _slowLoop = _slowHardware.Count > 0
            ? Task.Run(() => SlowLoopAsync(slowInterval ?? DefaultSlowInterval, _slowCts.Token))
            : Task.CompletedTask;
    }

    public SensorSnapshot Read()
    {
        UpdateAll(_fastHardware);

        // Os valores do armazenamento são os da última passada do laço lento.
        var values = new Dictionary<string, double>(_sensors.Count);
        foreach (var (sensor, info) in _sensors)
        {
            // Descarta "sem valor" (null) e valores inválidos (NaN, infinito).
            if (sensor.Value is float v && float.IsFinite(v))
                values[info.Id] = v;
        }
        return new SensorSnapshot(DateTimeOffset.Now, values);
    }

    public void Dispose()
    {
        // Para o laço lento ANTES de fechar o LHM (ele pode estar no meio de um Update do HD).
        _slowCts.Cancel();
        try { _slowLoop.Wait(TimeSpan.FromSeconds(10)); }
        catch (AggregateException) { }
        _slowCts.Dispose();
        _computer.Close();
    }

    private async Task SlowLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    UpdateAll(_slowHardware);
                }
                catch (Exception)
                {
                    // Mantém os últimos valores e tenta de novo no próximo ciclo.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Parada normal.
        }
    }

    private void UpdateAll(List<(IHardware Hardware, string Id)> hardware)
    {
        foreach (var (hw, id) in hardware)
        {
            long start = Stopwatch.GetTimestamp();
            hw.Update();
            _updateDurations[id] = Stopwatch.GetElapsedTime(start);
        }
    }

    private void Register(IHardware hw, string? parentId, Func<string, bool> include, bool pawnIo)
    {
        string id = hw.Identifier.ToString();
        if (!include(id))
            return;

        var kind = MapComponent(hw.HardwareType, id);

        // Discos vão para o laço lento; o resto, para o rápido.
        (kind == ComponentKind.Storage ? _slowHardware : _fastHardware).Add((hw, id));
        _components.Add(new ComponentInfo(id, hw.Name, kind, parentId));

        foreach (var sensor in hw.Sensors)
        {
            // Sem PawnIO, o consumo da CPU vem como 0 W em vez de "sem valor". Melhor esconder.
            if (!pawnIo && kind == ComponentKind.Cpu && sensor.SensorType == SensorType.Power)
                continue;

            var info = new SensorInfo(sensor.Identifier.ToString(), id, sensor.Name, MapSensor(sensor.SensorType));
            _sensors.Add((sensor, info));
            _sensorInfos.Add(info);
        }

        foreach (var sub in hw.SubHardware)
            Register(sub, id, include, pawnIo);
    }

    private static void UpdateTree(IHardware hw)
    {
        hw.Update();
        foreach (var sub in hw.SubHardware)
            UpdateTree(sub);
    }

    private static ComponentKind MapComponent(HardwareType type, string id) => type switch
    {
        HardwareType.Cpu => ComponentKind.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => ComponentKind.Gpu,
        HardwareType.Memory when id == "/vram" => ComponentKind.VirtualMemory,
        HardwareType.Memory when id.StartsWith("/memory/dimm", StringComparison.Ordinal) => ComponentKind.MemoryModule,
        HardwareType.Memory => ComponentKind.Memory,
        HardwareType.Motherboard => ComponentKind.Motherboard,
        HardwareType.SuperIO => ComponentKind.SuperIO,
        HardwareType.Storage => ComponentKind.Storage,
        HardwareType.Network => ComponentKind.Network,
        _ => ComponentKind.Other
    };

    private static SensorKind MapSensor(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Load => SensorKind.Load,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Power => SensorKind.Power,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Current => SensorKind.Current,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Control => SensorKind.Control,
        SensorType.Level => SensorKind.Level,
        SensorType.Factor => SensorKind.Factor,
        SensorType.Data => SensorKind.Data,
        SensorType.SmallData => SensorKind.SmallData,
        SensorType.Throughput => SensorKind.Throughput,
        SensorType.Frequency => SensorKind.Frequency,
        SensorType.Flow => SensorKind.Flow,
        SensorType.Timing => SensorKind.Timing,
        _ => SensorKind.Other
    };
}