using System.Runtime.InteropServices;

namespace CoreWindows.Sensors.Nvidia;

/// <summary>
/// Única classe que fala com o nvml.dll (a biblioteca de gerenciamento que vem com o driver NVIDIA).
/// Cada função é procurada pelo nome ao carregar: a que não existir (driver antigo) devolve
/// <see cref="NvmlReturn.FunctionNotFound"/> em vez de derrubar o app.
/// </summary>
internal sealed class NvmlLibrary : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ViolationTime
    {
        public ulong ReferenceTime; // microssegundos
        public ulong Violation;     // nanossegundos
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvmlReturn SimpleFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvmlReturn TextFn([In, Out] byte[] buffer, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvmlReturn CountFn(out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvmlReturn HandleFn(uint index, out nint device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvmlReturn DeviceTextFn(nint device, [In, Out] byte[] buffer, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvmlReturn DeviceUIntFn(nint device, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvmlReturn DeviceKindUIntFn(nint device, int kind, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvmlReturn DeviceULongFn(nint device, out ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvmlReturn UtilizationFn(nint device, out Utilization utilization);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvmlReturn ViolationFn(nint device, int policy, out ViolationTime time);

    private const int TextBufferSize = 96; // o maior texto do NVML que usamos (nome da placa, formato novo)

    private readonly SimpleFn? _init;
    private readonly SimpleFn? _shutdown;
    private readonly TextFn? _driverVersion;
    private readonly TextFn? _nvmlVersion;
    private readonly CountFn? _count;
    private readonly HandleFn? _handleByIndex;
    private readonly DeviceTextFn? _name;
    private readonly DeviceUIntFn? _powerUsage;
    private readonly DeviceUIntFn? _powerLimit;
    private readonly DeviceKindUIntFn? _temperature;
    private readonly DeviceKindUIntFn? _temperatureThreshold;
    private readonly DeviceKindUIntFn? _clock;
    private readonly DeviceULongFn? _currentReasons;
    private readonly DeviceULongFn? _supportedReasons;
    private readonly UtilizationFn? _utilization;
    private readonly ViolationFn? _violation;

    private nint _module;
    private bool _initialized;

    private NvmlLibrary(nint module, string path)
    {
        _module = module;
        Path = path;

        // Quando a função mudou de nome, vale o primeiro que existir (o novo antes do antigo).
        _init = Bind<SimpleFn>("nvmlInit_v2", "nvmlInit");
        _shutdown = Bind<SimpleFn>("nvmlShutdown");
        _driverVersion = Bind<TextFn>("nvmlSystemGetDriverVersion");
        _nvmlVersion = Bind<TextFn>("nvmlSystemGetNVMLVersion");
        _count = Bind<CountFn>("nvmlDeviceGetCount_v2", "nvmlDeviceGetCount");
        _handleByIndex = Bind<HandleFn>("nvmlDeviceGetHandleByIndex_v2", "nvmlDeviceGetHandleByIndex");
        _name = Bind<DeviceTextFn>("nvmlDeviceGetName");
        _powerUsage = Bind<DeviceUIntFn>("nvmlDeviceGetPowerUsage");
        _powerLimit = Bind<DeviceUIntFn>("nvmlDeviceGetEnforcedPowerLimit");
        _temperature = Bind<DeviceKindUIntFn>("nvmlDeviceGetTemperature");
        _temperatureThreshold = Bind<DeviceKindUIntFn>("nvmlDeviceGetTemperatureThreshold");
        _clock = Bind<DeviceKindUIntFn>("nvmlDeviceGetClockInfo");
        _currentReasons = Bind<DeviceULongFn>(
            "nvmlDeviceGetCurrentClocksEventReasons", "nvmlDeviceGetCurrentClocksThrottleReasons");
        _supportedReasons = Bind<DeviceULongFn>(
            "nvmlDeviceGetSupportedClocksEventReasons", "nvmlDeviceGetSupportedClocksThrottleReasons");
        _utilization = Bind<UtilizationFn>("nvmlDeviceGetUtilizationRates");
        _violation = Bind<ViolationFn>("nvmlDeviceGetViolationStatus");
    }

    /// <summary>Caminho do arquivo carregado.</summary>
    public string Path { get; }

    /// <summary>
    /// Carrega e inicia o NVML. Devolve nulo (com o motivo) se não houver driver NVIDIA, se o arquivo
    /// não carregar ou se o nvmlInit falhar. O caminho é sempre completo: nunca procura o nvml.dll
    /// na pasta do app ou no PATH (o app roda como administrador).
    /// </summary>
    /// <param name="explicitPath">Só para testes: um arquivo específico no lugar dos caminhos padrão.</param>
    public static NvmlLibrary? TryLoad(string? explicitPath, out string? reason)
    {
        foreach (string path in explicitPath is null ? DefaultPaths() : [explicitPath])
        {
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out nint module))
                continue;

            var library = new NvmlLibrary(module, path);
            if (library._init is null || library._shutdown is null)
            {
                library.Dispose();
                reason = $"{path} não tem as funções básicas do NVML.";
                return null;
            }

            var code = library._init();
            if (code != NvmlReturn.Success)
            {
                library.Dispose();
                reason = $"O NVML não iniciou: {NvmlData.ErrorText(code)}.";
                return null;
            }

            library._initialized = true;
            reason = null;
            return library;
        }

        reason = "nvml.dll não encontrado (sem driver NVIDIA neste PC).";
        return null;
    }

    private static IEnumerable<string> DefaultPaths()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            yield break;

        // Drivers atuais instalam em System32; os antigos, na pasta do nvidia-smi.
        yield return System.IO.Path.Combine(Environment.SystemDirectory, "nvml.dll");

        if (Environment.GetEnvironmentVariable("ProgramW6432") is { Length: > 0 } programFiles)
            yield return System.IO.Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvml.dll");
    }

    public string? DriverVersion() => Text(_driverVersion);

    public string? NvmlVersion() => Text(_nvmlVersion);

    public NvmlReturn DeviceCount(out uint count)
    {
        count = 0;
        var check = Check(_count);
        return check != NvmlReturn.Success ? check : _count!(out count);
    }

    public NvmlReturn DeviceHandle(uint index, out nint device)
    {
        device = 0;
        var check = Check(_handleByIndex);
        return check != NvmlReturn.Success ? check : _handleByIndex!(index, out device);
    }

    public string? DeviceName(nint device)
    {
        if (Check(_name) != NvmlReturn.Success)
            return null;

        var buffer = new byte[TextBufferSize];
        return _name!(device, buffer, (uint)buffer.Length) == NvmlReturn.Success ? NvmlData.TextFrom(buffer) : null;
    }

    /// <summary>Consumo atual, em miliwatts.</summary>
    public NvmlReturn PowerUsage(nint device, out uint milliwatts) => Call(_powerUsage, device, out milliwatts);

    /// <summary>Limite de potência em vigor, em miliwatts.</summary>
    public NvmlReturn PowerLimit(nint device, out uint milliwatts) => Call(_powerLimit, device, out milliwatts);

    /// <summary>Temperatura do núcleo da GPU, em °C.</summary>
    public NvmlReturn Temperature(nint device, out uint celsius) => Call(_temperature, device, kind: 0, out celsius);

    public NvmlReturn TemperatureThreshold(nint device, NvmlTemperatureThreshold kind, out uint celsius)
        => Call(_temperatureThreshold, device, (int)kind, out celsius);

    public NvmlReturn Clock(nint device, NvmlClock kind, out uint megahertz)
        => Call(_clock, device, (int)kind, out megahertz);

    public NvmlReturn CurrentReasons(nint device, out ulong reasons) => Call(_currentReasons, device, out reasons);

    public NvmlReturn SupportedReasons(nint device, out ulong reasons) => Call(_supportedReasons, device, out reasons);

    /// <summary>Utilização do núcleo da GPU, em %.</summary>
    public NvmlReturn GpuUtilization(nint device, out uint percent)
    {
        percent = 0;
        var check = Check(_utilization);
        if (check != NvmlReturn.Success)
            return check;

        var code = _utilization!(device, out var utilization);
        percent = utilization.Gpu;
        return code;
    }

    /// <summary>Tempo acumulado (em nanossegundos) com o clock segurado por uma causa.</summary>
    public NvmlReturn ViolationNanoseconds(nint device, NvmlPerfPolicy policy, out ulong nanoseconds)
    {
        nanoseconds = 0;
        var check = Check(_violation);
        if (check != NvmlReturn.Success)
            return check;

        var code = _violation!(device, (int)policy, out var time);
        nanoseconds = time.Violation;
        return code;
    }

    public void Dispose()
    {
        if (_initialized)
        {
            _initialized = false;
            try { _shutdown?.Invoke(); }
            catch (Exception) { /* fechando: nada a fazer */ }
        }

        if (_module != 0)
        {
            NativeLibrary.Free(_module);
            _module = 0;
        }
    }

    private T? Bind<T>(params string[] names) where T : Delegate
    {
        foreach (string name in names)
        {
            if (NativeLibrary.TryGetExport(_module, name, out nint address))
                return Marshal.GetDelegateForFunctionPointer<T>(address);
        }
        return null;
    }

    /// <summary>Depois de fechado, nada mais chama o arquivo (que já saiu da memória).</summary>
    private NvmlReturn Check(Delegate? function)
        => !_initialized ? NvmlReturn.Uninitialized
        : function is null ? NvmlReturn.FunctionNotFound
        : NvmlReturn.Success;

    private NvmlReturn Call(DeviceUIntFn? function, nint device, out uint value)
    {
        value = 0;
        var check = Check(function);
        return check != NvmlReturn.Success ? check : function!(device, out value);
    }

    private NvmlReturn Call(DeviceKindUIntFn? function, nint device, int kind, out uint value)
    {
        value = 0;
        var check = Check(function);
        return check != NvmlReturn.Success ? check : function!(device, kind, out value);
    }

    private NvmlReturn Call(DeviceULongFn? function, nint device, out ulong value)
    {
        value = 0;
        var check = Check(function);
        return check != NvmlReturn.Success ? check : function!(device, out value);
    }

    private string? Text(TextFn? function)
    {
        if (Check(function) != NvmlReturn.Success)
            return null;

        var buffer = new byte[TextBufferSize];
        return function!(buffer, (uint)buffer.Length) == NvmlReturn.Success ? NvmlData.TextFrom(buffer) : null;
    }
}