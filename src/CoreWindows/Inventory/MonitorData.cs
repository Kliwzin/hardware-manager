using System.Text.RegularExpressions;

namespace CoreWindows.Inventory;

// Dados crus de cada fonte, antes do cruzamento.
public sealed record MonitorIdentity(string InstanceName, string ManufacturerCode, string ProductCode, string Name, string Serial);
public sealed record MonitorDimensions(string InstanceName, int WidthCm, int HeightCm);
public sealed record MonitorLink(string InstanceName, long Technology);
public sealed record DisplayOutput(string DevicePath, DisplayMode Mode, bool IsPrimary);

/// <summary>Decodifica os dados crus dos monitores e cruza WMI com as APIs de vídeo. Sem acesso a hardware.</summary>
public static class MonitorData
{
    /// <summary>Os textos do EDID vêm como arrays de UInt16, um caractere por posição, completados com zeros.</summary>
    public static string DecodeText(IEnumerable<ushort>? codes)
    {
        if (codes is null)
            return "";

        var chars = codes.TakeWhile(c => c != 0).Select(c => (char)c).ToArray();
        return new string(chars).Trim();
    }

    /// <summary>Muitos fabricantes gravam "0" ou "00000000" no lugar do número de série.</summary>
    public static string DecodeSerial(IEnumerable<ushort>? codes)
    {
        string text = DecodeText(codes);
        return text.All(c => c == '0') ? "" : text;
    }

    /// <summary>"DISPLAY\GSM5B08\5&amp;2a3b&amp;0&amp;UID4353_0" → "DISPLAY\GSM5B08\5&amp;2A3B&amp;0&amp;UID4353"</summary>
    public static string KeyFromWmi(string instanceName) =>
        Regex.Replace(instanceName, @"_\d+$", "").ToUpperInvariant();

    /// <summary>"\\?\DISPLAY#GSM5B08#5&amp;2a3b&amp;0&amp;UID4353#{e6f0...}" → "DISPLAY\GSM5B08\5&amp;2A3B&amp;0&amp;UID4353"</summary>
    public static string KeyFromDevicePath(string devicePath)
    {
        const string prefix = @"\\?\";
        string path = devicePath.StartsWith(prefix, StringComparison.Ordinal)
            ? devicePath[prefix.Length..]
            : devicePath;

        int guid = path.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0)
            path = path[..guid];

        return path.Replace('#', '\\').ToUpperInvariant();
    }

    public static IReadOnlyList<MonitorInventory> Merge(
        IReadOnlyList<DisplayOutput> outputs,
        IReadOnlyList<MonitorIdentity> identities,
        IReadOnlyList<MonitorDimensions> dimensions,
        IReadOnlyList<MonitorLink> links)
    {
        var identityByKey = ByKey(identities, i => i.InstanceName);
        var dimensionsByKey = ByKey(dimensions, d => d.InstanceName);
        var linkByKey = ByKey(links, l => l.InstanceName);

        MonitorInventory Build(string key, DisplayOutput? output)
        {
            var identity = identityByKey.GetValueOrDefault(key);
            var size = dimensionsByKey.GetValueOrDefault(key);
            var link = linkByKey.GetValueOrDefault(key);
            string manufacturer = InventoryNames.MonitorManufacturer(identity?.ManufacturerCode ?? "");

            return new MonitorInventory(
                BuildName(identity, manufacturer),
                manufacturer,
                identity?.Serial ?? "",
                link is null ? "Desconhecida" : InventoryNames.MonitorConnection(link.Technology),
                size is { WidthCm: > 0, HeightCm: > 0 } ? new PhysicalSize(size.WidthCm, size.HeightCm) : null,
                output?.Mode,
                output?.IsPrimary ?? false);
        }

        // OrderByDescending é estável: depois do principal, mantém a ordem do Windows.
        var result = outputs
            .OrderByDescending(o => o.IsPrimary)
            .Select(o => Build(KeyFromDevicePath(o.DevicePath), o))
            .ToList();

        // Conectados, mas sem saída ativa (ex.: Windows em "mostrar só na tela 2").
        var onDesktop = outputs.Select(o => KeyFromDevicePath(o.DevicePath)).ToHashSet();
        result.AddRange(identityByKey.Keys.Where(k => !onDesktop.Contains(k)).Select(k => Build(k, null)));

        return result;
    }

    private static Dictionary<string, T> ByKey<T>(IEnumerable<T> items, Func<T, string> instanceName) =>
        items.GroupBy(i => KeyFromWmi(instanceName(i))).ToDictionary(g => g.Key, g => g.First());

    private static string BuildName(MonitorIdentity? identity, string manufacturer)
    {
        if (!string.IsNullOrEmpty(identity?.Name))
            return identity.Name;

        // Telas de notebook costumam não ter nome no EDID.
        string fallback = $"{manufacturer} {identity?.ProductCode}".Trim();
        return fallback.Length > 0 ? fallback : "Monitor sem identificação";
    }
}