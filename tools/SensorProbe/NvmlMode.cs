using System.Diagnostics;
using System.Globalization;
using System.Text;
using CoreWindows.Sensors.Nvidia;

/// <summary>
/// Investiga o que o NVML informa neste PC: primeiro os dados fixos de cada GPU NVIDIA, depois uma
/// leitura por segundo (para rodar em repouso e de novo com um jogo ou teste de estresse aberto).
/// Não precisa de administrador. Uso: -- nvml [segundos] [rótulo]   (padrão: 60 segundos, rótulo "nvml")
/// </summary>
static class NvmlMode
{
    private static readonly (NvmlPerfPolicy Policy, string Label)[] Policies =
    [
        (NvmlPerfPolicy.Power, "potência"),
        (NvmlPerfPolicy.Thermal, "térmico"),
        (NvmlPerfPolicy.BoardLimit, "limite da placa"),
        (NvmlPerfPolicy.Reliability, "confiabilidade"),
        (NvmlPerfPolicy.TotalBaseClocks, "abaixo do clock base"),
    ];

    public static void Run(string[] args, string? libraryPath = null)
    {
        int seconds = args.Length > 0 && int.TryParse(args[0], out int parsed) && parsed > 0 ? parsed : 60;
        string tag = args.Length > 1 ? args[1] : "nvml";
        var report = new StringBuilder();

        void Line(string text = "")
        {
            Console.WriteLine(text);
            report.AppendLine(text);
        }

        Line($"SensorProbe NVML — {Environment.MachineName} — {DateTime.Now:yyyy-MM-dd HH:mm}");

        long start = Stopwatch.GetTimestamp();
        using var session = NvmlSession.TryOpen(out string? reason, libraryPath);
        double openMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        if (session is null)
        {
            Line($"NVML indisponível: {reason}");
            Line("Neste PC o app funciona normalmente, só sem os extras da NVIDIA.");
            Save(report, tag);
            return;
        }

        Line($"Biblioteca: {session.LibraryPath}");
        Line($"Driver: {session.DriverVersion} | NVML: {session.NvmlVersion} | abrir: {openMs:0} ms");
        Line($"GPUs: {session.Gpus.Count}");

        foreach (var gpu in session.Gpus)
            DescribeStatic(gpu, Line);

        if (session.Gpus.Count > 0)
            Watch(session.Gpus, seconds, Line);

        Save(report, tag);
    }

    private static void DescribeStatic(NvmlGpu gpu, Action<string> line)
    {
        line("");
        line($"[{gpu.Index}] {gpu.Name}");
        line($"    Consumo agora:            {Show(gpu.ReadPowerWatts(), "0.0", "W")}");
        line($"    Limite de potência:       {Show(gpu.ReadPowerLimitWatts(), "0.0", "W")}");
        line($"    Temperatura:              {Show(gpu.ReadTemperature(), "0", "°C")}");
        line($"    Limite: desligamento      {Show(gpu.ReadTemperatureThreshold(NvmlTemperatureThreshold.Shutdown), "0", "°C")}");
        line($"    Limite: redução (hw)      {Show(gpu.ReadTemperatureThreshold(NvmlTemperatureThreshold.Slowdown), "0", "°C")}");
        line($"    Limite: máximo da GPU     {Show(gpu.ReadTemperatureThreshold(NvmlTemperatureThreshold.GpuMax), "0", "°C")}");
        line($"    Limite: máximo da memória {Show(gpu.ReadTemperatureThreshold(NvmlTemperatureThreshold.MemoryMax), "0", "°C")}");
        line($"    Utilização:               {Show(gpu.ReadUtilization(), "0", "%")}");
        line($"    Clock do núcleo:          {Show(gpu.ReadClockMhz(NvmlClock.Graphics), "0", "MHz")}");
        line($"    Clock da memória:         {Show(gpu.ReadClockMhz(NvmlClock.Memory), "0", "MHz")}");

        var supported = gpu.ReadSupportedThrottleReasons();
        line($"    Motivos que a placa informa: {(supported.Ok ? Reasons(supported.Value) : Error(supported.Code))}");

        var current = gpu.ReadThrottleReasons();
        line($"    Motivos agora:               {(current.Ok ? Reasons(current.Value) : Error(current.Code))}");

        foreach (var (policy, label) in Policies)
        {
            var time = gpu.ReadViolationTime(policy);
            line($"    Tempo segurado ({label}): {(time.Ok ? $"{time.Value.TotalSeconds:0.000} s" : Error(time.Code))}");
        }
    }

    private static void Watch(IReadOnlyList<NvmlGpu> gpus, int seconds, Action<string> line)
    {
        line("");
        line($"Acompanhando por {seconds} s (uma leitura por segundo). Ctrl+C interrompe sem salvar.");
        line("hora      gpu  uso   temp   clock      consumo   segurado no último segundo / motivos");

        var watched = gpus.Select(gpu => new Watched(gpu)).ToList();
        double totalMs = 0, worstMs = 0;

        for (int second = 0; second < seconds; second++)
        {
            Thread.Sleep(1000);
            long start = Stopwatch.GetTimestamp();
            var lines = watched.Select(w => w.Read()).ToList();
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            totalMs += ms;
            worstMs = Math.Max(worstMs, ms);
            foreach (string text in lines)
                line(text);
        }

        line("");
        line($"Custo de cada leitura (todas as consultas, todas as GPUs): média {totalMs / seconds:0.00} ms, pior {worstMs:0.00} ms.");
        foreach (var w in watched)
            w.Summarize(line);
    }

    /// <summary>Uma GPU durante o acompanhamento: guarda o que é preciso para o resumo final.</summary>
    private sealed class Watched(NvmlGpu gpu)
    {
        private readonly Dictionary<NvmlPerfPolicy, TimeSpan> _lastViolation = [];
        private readonly Dictionary<NvmlPerfPolicy, TimeSpan> _totalViolation = [];
        private readonly Dictionary<string, int> _reasonSeconds = [];
        private readonly List<double> _power = [];
        private int _readings;

        public string Read()
        {
            _readings++;
            var utilization = gpu.ReadUtilization();
            var temperature = gpu.ReadTemperature();
            var clock = gpu.ReadClockMhz(NvmlClock.Graphics);
            var power = gpu.ReadPowerWatts();
            var reasons = gpu.ReadThrottleReasons();

            if (power.Ok)
                _power.Add(power.Value);

            var notes = new List<string>();
            foreach (var (policy, label) in Policies)
            {
                var time = gpu.ReadViolationTime(policy);
                if (!time.Ok)
                    continue;

                if (_lastViolation.TryGetValue(policy, out var before) && time.Value > before)
                {
                    var delta = time.Value - before;
                    _totalViolation[policy] = _totalViolation.GetValueOrDefault(policy) + delta;
                    notes.Add($"{label} {delta.TotalMilliseconds:0} ms");
                }
                _lastViolation[policy] = time.Value;
            }

            if (reasons.Ok)
            {
                foreach (string reason in NvmlData.Describe(reasons.Value))
                    _reasonSeconds[reason] = _reasonSeconds.GetValueOrDefault(reason) + 1;
                notes.Add(Reasons(reasons.Value));
            }

            return $"{DateTime.Now:HH:mm:ss}  [{gpu.Index}]  {Cell(utilization, "0", "%"),4}  {Cell(temperature, "0", "°C"),5}  " +
                   $"{Cell(clock, "0", "MHz"),8}  {Cell(power, "0.0", "W"),8}   {string.Join(" | ", notes)}";
        }

        public void Summarize(Action<string> line)
        {
            line("");
            line($"[{gpu.Index}] {gpu.Name} — {_readings} leituras");

            line(_power.Count > 0
                ? $"    Consumo: mín {_power.Min():0.0} W | média {_power.Average():0.0} W | máx {_power.Max():0.0} W"
                : "    Consumo: sem leitura");

            if (_reasonSeconds.Count == 0)
                line("    Motivos de throttling: nenhum apareceu (ou a placa não informa)");
            foreach (var (reason, count) in _reasonSeconds.OrderByDescending(pair => pair.Value))
                line($"    Motivo \"{reason}\": em {count} de {_readings} leituras");

            foreach (var (policy, label) in Policies)
            {
                if (_totalViolation.TryGetValue(policy, out var total))
                    line($"    Tempo segurado ({label}): {total.TotalSeconds:0.000} s no período");
            }
        }
    }

    private static string Reasons(NvmlThrottleReasons reasons)
    {
        var labels = NvmlData.Describe(reasons);
        return labels.Count == 0 ? "nenhum" : $"{string.Join(", ", labels)} (0x{(ulong)reasons:X})";
    }

    private static string Show(NvmlResult<double> result, string format, string unit)
        => result.Ok ? $"{result.Value.ToString(format, CultureInfo.CurrentCulture)} {unit}" : Error(result.Code);

    private static string Cell(NvmlResult<double> result, string format, string unit)
        => result.Ok ? $"{result.Value.ToString(format, CultureInfo.CurrentCulture)}{unit}" : "—";

    private static string Error(NvmlReturn code) => $"NÃO — {NvmlData.ErrorText(code)}";

    private static void Save(StringBuilder report, string tag)
    {
        // Mesmo lugar dos outros relatórios: docs/sensores/<PC>-<rótulo>.txt, relativo à pasta de onde rodou.
        string dir = Path.Combine(Environment.CurrentDirectory, "docs", "sensores");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"{Environment.MachineName}-{tag}.txt");
        File.WriteAllText(file, report.ToString(), Encoding.UTF8);
        Console.WriteLine($"Salvo em: {file}");
    }
}