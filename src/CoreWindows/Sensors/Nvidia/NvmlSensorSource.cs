namespace CoreWindows.Sensors.Nvidia;

/// <summary>
/// Extras das GPUs NVIDIA que o LibreHardwareMonitor não entrega: o consumo em watts (como um sensor
/// normal, pendurado no componente da GPU que o LHM já criou) e os motivos de throttling (à parte,
/// em <see cref="ThrottleReasons"/>, porque são um conjunto de bits e não um número).
/// Não cria componentes. Depois de criada, nenhuma leitura lança exceção.
/// </summary>
public sealed class NvmlSensorSource : ISensorSource
{
    public const string PowerSensorName = "GPU Power";

    /// <summary>Nome do sensor de consumo que o próprio LHM cria (com a mesma leitura do NVML) em algumas placas.</summary>
    private const string LhmPowerSensorName = "GPU Package";

    private sealed class Entry(INvmlGpu gpu, string componentId, SensorInfo? power)
    {
        public INvmlGpu Gpu { get; } = gpu;
        public string ComponentId { get; } = componentId;
        public SensorInfo? Power { get; } = power;
        public bool ReadsReasons { get; set; } = true;
    }

    private readonly List<Entry> _entries = [];
    private readonly List<SensorInfo> _sensors = [];
    private readonly IDisposable? _owner;
    private volatile IReadOnlyDictionary<string, NvmlThrottleReasons> _reasons =
        new Dictionary<string, NvmlThrottleReasons>();

    /// <param name="gpus">As GPUs do NVML, na ordem dele.</param>
    /// <param name="components">Componentes da fonte principal (o LHM): é neles que os sensores são pendurados.</param>
    /// <param name="sensors">Sensores da fonte principal, para não criar um consumo que ela já tem.</param>
    /// <param name="owner">Descartado junto com esta fonte (a sessão do NVML).</param>
    public NvmlSensorSource(
        IReadOnlyList<INvmlGpu> gpus,
        IReadOnlyList<ComponentInfo> components,
        IReadOnlyList<SensorInfo> sensors,
        IDisposable? owner = null)
    {
        _owner = owner;
        var componentIds = NvmlData.Match(components, [.. gpus.Select(gpu => gpu.Name)]);
        var initial = new Dictionary<string, NvmlThrottleReasons>();

        for (int i = 0; i < gpus.Count; i++)
        {
            if (componentIds[i] is not { } componentId)
                continue; // GPU que o LHM não mostra: não há onde pendurar os dados

            var gpu = gpus[i];

            // Consumo: só se a placa informa de verdade (uma leitura de teste) e o LHM ainda não tem.
            SensorInfo? power = null;
            bool lhmHasPower = sensors.Any(s =>
                s.ComponentId == componentId && s.Kind == SensorKind.Power && s.Name == LhmPowerSensorName);
            if (!lhmHasPower && Safe(gpu.ReadPowerWatts) is { Ok: true })
                power = new SensorInfo($"{componentId}/nvml/power", componentId, PowerSensorName, SensorKind.Power);

            var reasons = Safe(gpu.ReadThrottleReasons);
            if (power is null && !reasons.Ok)
                continue; // esta placa não informa nada do que queremos

            var entry = new Entry(gpu, componentId, power) { ReadsReasons = reasons.Ok };
            _entries.Add(entry);
            if (power is not null)
                _sensors.Add(power);
            if (reasons.Ok)
                initial[componentId] = reasons.Value;
        }

        _reasons = initial;
    }

    /// <summary>
    /// Abre o NVML e monta a fonte. Devolve nulo — sem exceção e sem deixar nada aberto — quando não há
    /// driver NVIDIA, quando o NVML falha ou quando nenhuma placa informa consumo nem motivos de throttling.
    /// </summary>
    public static NvmlSensorSource? TryCreate(IReadOnlyList<ComponentInfo> components, IReadOnlyList<SensorInfo> sensors)
    {
        NvmlSession? session = null;
        try
        {
            session = NvmlSession.TryOpen(out _);
            if (session is null)
                return null;

            var source = new NvmlSensorSource(session.Gpus, components, sensors, session);
            if (!source.IsEmpty)
                return source;

            source.Dispose();
            return null;
        }
        catch (Exception)
        {
            session?.Dispose();
            return null;
        }
    }

    /// <summary>Nenhuma GPU com dados: não vale a pena manter o NVML aberto.</summary>
    public bool IsEmpty => _entries.Count == 0;

    public IReadOnlyList<ComponentInfo> Components => [];

    public IReadOnlyList<SensorInfo> Sensors => _sensors;

    /// <summary>
    /// Motivos de throttling da última leitura, por id de componente (ex.: "/gpu-nvidia/0").
    /// GPU que não informa (ou cuja leitura falhou) fica de fora. Pode ser lido de qualquer thread.
    /// </summary>
    public IReadOnlyDictionary<string, NvmlThrottleReasons> ThrottleReasons => _reasons;

    public SensorSnapshot Read()
    {
        var values = new Dictionary<string, double>(_sensors.Count);
        var reasons = new Dictionary<string, NvmlThrottleReasons>(_entries.Count);

        foreach (var entry in _entries)
        {
            if (entry.Power is { } power
                && Safe(entry.Gpu.ReadPowerWatts) is { Ok: true } watts
                && double.IsFinite(watts.Value))
            {
                values[power.Id] = watts.Value;
            }

            if (entry.ReadsReasons)
            {
                var result = Safe(entry.Gpu.ReadThrottleReasons);
                if (result.Ok)
                    reasons[entry.ComponentId] = result.Value;
                else if (result.Code is NvmlReturn.NotSupported or NvmlReturn.FunctionNotFound)
                    entry.ReadsReasons = false; // não vai passar a funcionar: para de perguntar
            }
        }

        _reasons = reasons;
        return new SensorSnapshot(DateTimeOffset.Now, values);
    }

    public void Dispose() => _owner?.Dispose();

    /// <summary>Uma consulta que lança exceção vale como "erro desconhecido": nada sai desta classe.</summary>
    private static NvmlResult<T> Safe<T>(Func<NvmlResult<T>> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return new NvmlResult<T>(NvmlReturn.Unknown, default!);
        }
    }
}