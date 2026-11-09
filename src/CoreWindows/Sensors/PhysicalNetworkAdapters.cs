using System.Management;

namespace CoreWindows.Sensors;

/// <summary>Separa adaptadores de rede reais das camadas virtuais do Windows.</summary>
public static class PhysicalNetworkAdapters
{
    /// <summary>GUIDs dos adaptadores físicos, segundo o WMI. Ex.: "{A6A6D982-...}".</summary>
    public static HashSet<string> GetGuids()
    {
        var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var searcher = new ManagementObjectSearcher(
            "SELECT GUID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE");

        foreach (ManagementObject adapter in searcher.Get())
        {
            using (adapter)
            {
                if (adapter["GUID"] is string guid)
                    guids.Add(guid);
            }
        }
        return guids;
    }

    /// <summary>"/nic/%7BA6A6...%7D" → "{A6A6...}". Retorna null se não for id de rede.</summary>
    public static string? GuidFromIdentifier(string id) =>
        id.StartsWith("/nic/", StringComparison.Ordinal)
            ? Uri.UnescapeDataString(id["/nic/".Length..])
            : null;
}