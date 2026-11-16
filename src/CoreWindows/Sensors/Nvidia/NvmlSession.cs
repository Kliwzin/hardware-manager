namespace CoreWindows.Sensors.Nvidia;

/// <summary>
/// O que o <see cref="NvmlSensorSource"/> precisa de uma GPU. Existe para podermos testar a fonte
/// sem placa NVIDIA (como o ISensorSource faz com o SensorService).
/// </summary>
public interface INvmlGpu
{
    string Name { get; }

    /// <summary>Consumo atual da placa, em watts.</summary>
    NvmlResult<double> ReadPowerWatts();

    /// <summary>Motivos pelos quais o driver está segurando os clocks neste instante.</summary>
    NvmlResult<NvmlThrottleReasons> ReadThrottleReasons();
}

/// <summary>
/// O NVML aberto: lista as GPUs NVIDIA e mantém a biblioteca carregada até o Dispose.
/// Não precisa de administrador nem do PawnIO.
/// </summary>
public sealed class NvmlSession : IDisposable
{
    private readonly NvmlLibrary _library;

    private NvmlSession(NvmlLibrary library)
    {
        _library = library;
        DriverVersion = library.DriverVersion() ?? "";
        NvmlVersion = library.NvmlVersion() ?? "";

        var gpus = new List<NvmlGpu>();
        if (library.DeviceCount(out uint count) == NvmlReturn.Success)
        {
            for (uint i = 0; i < count; i++)
            {
                // Uma placa que não responde (ou sem permissão) fica de fora; as outras continuam.
                if (library.DeviceHandle(i, out nint device) != NvmlReturn.Success)
                    continue;

                gpus.Add(new NvmlGpu(library, device, (int)i, library.DeviceName(device) ?? $"GPU NVIDIA {i}"));
            }
        }
        Gpus = gpus;
    }

    public string LibraryPath => _library.Path;
    public string DriverVersion { get; }
    public string NvmlVersion { get; }
    public IReadOnlyList<NvmlGpu> Gpus { get; }

    /// <summary>
    /// Abre o NVML. Nunca lança exceção: sem driver NVIDIA (ou com qualquer falha) devolve nulo
    /// e explica em <paramref name="reason"/>.
    /// </summary>
    /// <param name="libraryPath">Só para testes: um arquivo específico no lugar do nvml.dll do driver.</param>
    public static NvmlSession? TryOpen(out string? reason, string? libraryPath = null)
    {
        try
        {
            var library = NvmlLibrary.TryLoad(libraryPath, out reason);
            if (library is null)
                return null;

            try
            {
                return new NvmlSession(library);
            }
            catch
            {
                library.Dispose();
                throw;
            }
        }
        catch (Exception ex)
        {
            reason = $"Falha ao abrir o NVML: {ex.Message}";
            return null;
        }
    }

    /// <summary>Fecha o NVML. Depois disso, as leituras das GPUs devolvem <see cref="NvmlReturn.Uninitialized"/>.</summary>
    public void Dispose() => _library.Dispose();
}

/// <summary>Uma GPU NVIDIA vista pelo NVML. Cada leitura devolve o código do NVML junto com o valor.</summary>
public sealed class NvmlGpu : INvmlGpu
{
    private readonly NvmlLibrary _library;
    private readonly nint _device;

    internal NvmlGpu(NvmlLibrary library, nint device, int index, string name)
    {
        _library = library;
        _device = device;
        Index = index;
        Name = name;
    }

    /// <summary>Posição da placa na lista do NVML.</summary>
    public int Index { get; }

    public string Name { get; }

    public NvmlResult<double> ReadPowerWatts()
        => new(_library.PowerUsage(_device, out uint milliwatts), milliwatts / 1000.0);

    /// <summary>Limite de potência em vigor, em watts.</summary>
    public NvmlResult<double> ReadPowerLimitWatts()
        => new(_library.PowerLimit(_device, out uint milliwatts), milliwatts / 1000.0);

    /// <summary>Temperatura do núcleo, em °C.</summary>
    public NvmlResult<double> ReadTemperature()
        => new(_library.Temperature(_device, out uint celsius), celsius);

    /// <summary>Um dos limites de temperatura da placa, em °C.</summary>
    public NvmlResult<double> ReadTemperatureThreshold(NvmlTemperatureThreshold kind)
        => new(_library.TemperatureThreshold(_device, kind, out uint celsius), celsius);

    /// <summary>Utilização do núcleo, em %.</summary>
    public NvmlResult<double> ReadUtilization()
        => new(_library.GpuUtilization(_device, out uint percent), percent);

    public NvmlResult<double> ReadClockMhz(NvmlClock clock)
        => new(_library.Clock(_device, clock, out uint megahertz), megahertz);

    public NvmlResult<NvmlThrottleReasons> ReadThrottleReasons()
        => new(_library.CurrentReasons(_device, out ulong reasons), (NvmlThrottleReasons)reasons);

    /// <summary>Quais motivos esta placa é capaz de informar.</summary>
    public NvmlResult<NvmlThrottleReasons> ReadSupportedThrottleReasons()
        => new(_library.SupportedReasons(_device, out ulong reasons), (NvmlThrottleReasons)reasons);

    /// <summary>
    /// Tempo acumulado com o clock segurado por uma causa. É um contador que só cresce:
    /// o que interessa é a diferença entre duas leituras.
    /// </summary>
    public NvmlResult<TimeSpan> ReadViolationTime(NvmlPerfPolicy policy)
        => new(_library.ViolationNanoseconds(_device, policy, out ulong nanoseconds),
               TimeSpan.FromTicks((long)(nanoseconds / 100)));
}