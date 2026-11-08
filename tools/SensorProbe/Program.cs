using System.Security.Principal;
using System.Text;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

Console.OutputEncoding = Encoding.UTF8;

// Rótulo do arquivo de saída: "sem-pawnio", "com-pawnio", etc.
string tag = args.Length > 0 ? args[0] : "teste";

bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
    .IsInRole(WindowsBuiltInRole.Administrator);

var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = true,
    IsMotherboardEnabled = true,
    IsControllerEnabled = true,
    IsStorageEnabled = true,
    IsNetworkEnabled = true,
};

var report = new StringBuilder();
int total = 0, withValue = 0;

try
{
    computer.Open();

    // Alguns sensores (ex.: carga da CPU) só têm valor depois de duas leituras.
    UpdateAll(computer.Hardware);
    Thread.Sleep(1000);
    UpdateAll(computer.Hardware);

    report.AppendLine($"SensorProbe — {Environment.MachineName} — {DateTime.Now:yyyy-MM-dd HH:mm}");
    report.AppendLine($"Administrador: {(isAdmin ? "sim" : "NÃO")}");
    report.AppendLine($"PawnIO: {PawnIoVersion() ?? "não instalado"}");
    report.AppendLine();

    foreach (var hw in computer.Hardware)
        Describe(hw, 0, report, ref total, ref withValue);

    report.AppendLine($"Total: {total} sensores — {withValue} com valor, {total - withValue} sem valor.");
}
finally
{
    computer.Close();
}

// Salva em docs/sensores/<PC>-<tag>.txt (relativo à pasta de onde você rodou)
string dir = Path.Combine(Environment.CurrentDirectory, "docs", "sensores");
Directory.CreateDirectory(dir);
string file = Path.Combine(dir, $"{Environment.MachineName}-{tag}.txt");
File.WriteAllText(file, report.ToString(), Encoding.UTF8);

Console.Write(report);
Console.WriteLine($"Salvo em: {file}");

// ---------- funções auxiliares ----------

static void UpdateAll(IEnumerable<IHardware> hardware)
{
    foreach (var hw in hardware)
    {
        hw.Update();
        UpdateAll(hw.SubHardware);
    }
}

static void Describe(IHardware hw, int depth, StringBuilder sb, ref int total, ref int withValue)
{
    string pad = new(' ', depth * 4);
    sb.AppendLine($"{pad}[{hw.HardwareType}] {hw.Name}");
    sb.AppendLine($"{pad}    id: {hw.Identifier}");

    foreach (var s in hw.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Index))
    {
        total++;
        if (s.Value.HasValue) withValue++;

        string value = s.Value.HasValue
            ? $"{s.Value.Value:0.##} {Unit(s.SensorType)}"
            : "— sem valor —";

        sb.AppendLine($"{pad}    {s.SensorType,-12} {s.Name,-40} {value}");
    }

    foreach (var sub in hw.SubHardware)
        Describe(sub, depth + 1, sb, ref total, ref withValue);

    sb.AppendLine();
}

static string Unit(SensorType type) => type switch
{
    SensorType.Temperature => "°C",
    SensorType.Load => "%",
    SensorType.Clock => "MHz",
    SensorType.Power => "W",
    SensorType.Voltage => "V",
    SensorType.Current => "A",
    SensorType.Fan => "RPM",
    SensorType.Control => "%",
    SensorType.Level => "%",
    SensorType.Data => "GB",
    SensorType.SmallData => "MB",
    SensorType.Throughput => "B/s",
    SensorType.Frequency => "Hz",
    SensorType.Flow => "L/h",
    _ => ""
};

static string? PawnIoVersion() =>
    Registry.GetValue(
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO",
        "DisplayVersion", null) as string;