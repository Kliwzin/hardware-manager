using System.Text;

namespace CoreWindows.Sensors.Nvidia;

/// <summary>Código de retorno das funções do NVML (nvmlReturn_t no nvml.h).</summary>
public enum NvmlReturn
{
    Success = 0,
    Uninitialized = 1,
    InvalidArgument = 2,
    NotSupported = 3,
    NoPermission = 4,
    AlreadyInitialized = 5,
    NotFound = 6,
    InsufficientSize = 7,
    InsufficientPower = 8,
    DriverNotLoaded = 9,
    Timeout = 10,
    IrqIssue = 11,
    LibraryNotFound = 12,
    FunctionNotFound = 13,
    CorruptedInfoRom = 14,
    GpuIsLost = 15,
    ResetRequired = 16,
    OperatingSystem = 17,
    LibRmVersionMismatch = 18,
    InUse = 19,
    Memory = 20,
    NoData = 21,
    VgpuEccNotSupported = 22,
    InsufficientResources = 23,
    FreqNotSupported = 24,
    ArgumentVersionMismatch = 25,
    Deprecated = 26,
    NotReady = 27,
    GpuNotFound = 28,
    InvalidState = 29,
    ResetTypeNotSupported = 30,
    Unknown = 999,
}

/// <summary>
/// Motivos pelos quais o driver está segurando os clocks da GPU (nvmlClocksEventReason* no nvml.h).
/// É um conjunto de bits: vários motivos podem valer ao mesmo tempo.
/// </summary>
[Flags]
public enum NvmlThrottleReasons : ulong
{
    None = 0,
    GpuIdle = 0x1,
    ApplicationsClocksSetting = 0x2,
    SwPowerCap = 0x4,
    HwSlowdown = 0x8,
    SyncBoost = 0x10,
    SwThermalSlowdown = 0x20,
    HwThermalSlowdown = 0x40,
    HwPowerBrakeSlowdown = 0x80,
    DisplayClockSetting = 0x100,
    BoardLimit = 0x200,
    Reliability = 0x400,
}

/// <summary>Limites de temperatura que a própria placa informa (nvmlTemperatureThresholds_t).</summary>
public enum NvmlTemperatureThreshold
{
    Shutdown = 0,
    Slowdown = 1,
    MemoryMax = 2,
    GpuMax = 3,
}

/// <summary>Contadores de "tempo com o clock segurado", por causa (nvmlPerfPolicyType_t).</summary>
public enum NvmlPerfPolicy
{
    Power = 0,
    Thermal = 1,
    BoardLimit = 3,
    LowUtilization = 4,
    Reliability = 5,
    TotalBaseClocks = 11,
}

public enum NvmlClock
{
    Graphics = 0,
    Memory = 2,
}

/// <summary>Resultado de uma consulta ao NVML: o código e, se ele for Success, o valor.</summary>
public readonly record struct NvmlResult<T>(NvmlReturn Code, T Value)
{
    public bool Ok => Code == NvmlReturn.Success;
}

/// <summary>Tudo do NVML que não precisa de placa de vídeo para funcionar (e por isso pode ser testado).</summary>
public static class NvmlData
{
    private static readonly (NvmlThrottleReasons Bit, string Label)[] Labels =
    [
        (NvmlThrottleReasons.GpuIdle, "GPU ociosa"),
        (NvmlThrottleReasons.ApplicationsClocksSetting, "clocks definidos por aplicativo"),
        (NvmlThrottleReasons.SwPowerCap, "limite de potência (software)"),
        (NvmlThrottleReasons.HwSlowdown, "redução de clock por hardware"),
        (NvmlThrottleReasons.SyncBoost, "sync boost"),
        (NvmlThrottleReasons.SwThermalSlowdown, "limite térmico (software)"),
        (NvmlThrottleReasons.HwThermalSlowdown, "limite térmico (hardware)"),
        (NvmlThrottleReasons.HwPowerBrakeSlowdown, "freio de potência (hardware)"),
        (NvmlThrottleReasons.DisplayClockSetting, "limitado pelo clock de vídeo"),
        (NvmlThrottleReasons.BoardLimit, "limite da placa"),
        (NvmlThrottleReasons.Reliability, "limite de confiabilidade"),
    ];

    /// <summary>
    /// Os motivos ligados, em texto. Bit que este código não conhece (driver mais novo) aparece como
    /// "motivo desconhecido (0x...)" em vez de sumir.
    /// </summary>
    public static IReadOnlyList<string> Describe(NvmlThrottleReasons reasons)
    {
        var result = new List<string>();
        var rest = reasons;

        foreach (var (bit, label) in Labels)
        {
            if ((reasons & bit) != 0)
            {
                result.Add(label);
                rest &= ~bit;
            }
        }

        if (rest != 0)
            result.Add($"motivo desconhecido (0x{(ulong)rest:X})");
        return result;
    }

    /// <summary>Explicação curta de um código de erro, para relatórios.</summary>
    public static string ErrorText(NvmlReturn code) => code switch
    {
        NvmlReturn.Success => "ok",
        NvmlReturn.NotSupported => "não suportado por esta placa/driver",
        NvmlReturn.FunctionNotFound => "função não existe neste driver",
        NvmlReturn.NoPermission => "sem permissão",
        NvmlReturn.Uninitialized => "NVML fechado",
        NvmlReturn.DriverNotLoaded => "driver NVIDIA não carregado",
        NvmlReturn.GpuIsLost => "a GPU parou de responder",
        NvmlReturn.NoData => "sem dados",
        _ => $"erro {code} ({(int)code})",
    };

    /// <summary>Texto de um buffer preenchido pelo NVML: vai até o primeiro zero.</summary>
    public static string TextFrom(ReadOnlySpan<byte> buffer)
    {
        int end = buffer.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? buffer : buffer[..end]).Trim();
    }

    /// <summary>
    /// Nome de GPU em forma comparável: o LHM e o NVML escrevem o mesmo modelo de jeitos diferentes
    /// ("NVIDIA GeForce RTX 4060" e "GeForce RTX 4060", conforme a versão do driver).
    /// </summary>
    public static string NormalizeName(string name)
    {
        var words = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.ToUpperInvariant())
            .ToList();

        if (words.Count > 1 && words[0] == "NVIDIA")
            words.RemoveAt(0);
        return string.Join(' ', words);
    }

    /// <summary>
    /// Liga cada GPU do NVML (pela posição na lista de nomes) ao componente do LHM correspondente.
    /// Devolve, para cada GPU do NVML, o id do componente ou nulo se não houver par.
    /// Regra: primeiro pelo nome (duas placas iguais formam pares na ordem em que aparecem); o que sobrar
    /// dos dois lados só é ligado pela ordem se sobrar a mesma quantidade — na dúvida, fica sem par.
    /// </summary>
    public static IReadOnlyList<string?> Match(IReadOnlyList<ComponentInfo> components, IReadOnlyList<string> nvmlNames)
    {
        var candidates = components
            .Where(c => c.Kind == ComponentKind.Gpu && c.Id.StartsWith("/gpu-nvidia/", StringComparison.Ordinal))
            .ToList();

        var result = new string?[nvmlNames.Count];

        for (int i = 0; i < nvmlNames.Count; i++)
        {
            string wanted = NormalizeName(nvmlNames[i]);
            int found = candidates.FindIndex(c => NormalizeName(c.Name) == wanted);
            if (found >= 0)
            {
                result[i] = candidates[found].Id;
                candidates.RemoveAt(found);
            }
        }

        var unmatched = Enumerable.Range(0, result.Length).Where(i => result[i] is null).ToList();
        if (unmatched.Count == candidates.Count)
        {
            for (int k = 0; k < unmatched.Count; k++)
                result[unmatched[k]] = candidates[k].Id;
        }
        return result;
    }
}