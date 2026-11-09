using System.Globalization;

namespace CoreWindows.Inventory;

public static class InventoryFormat
{
    /// <param name="binary">
    /// true = base 1024 (como o Windows mostra RAM e volumes);
    /// false = base 1000 (como o fabricante anuncia discos: "4 TB").
    /// </param>
    public static string Bytes(double bytes, bool binary = true, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        double step = binary ? 1024 : 1000;
        string[] units = ["B", "KB", "MB", "GB", "TB"];

        int i = 0;
        while (bytes >= step && i < units.Length - 1)
        {
            bytes /= step;
            i++;
        }
        return $"{bytes.ToString(i == 0 ? "0" : "0.#", provider)} {units[i]}";
    }

    public static string LinkSpeed(long bitsPerSecond, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        if (bitsPerSecond <= 0)
            return "—";

        return bitsPerSecond >= 1_000_000_000
            ? $"{(bitsPerSecond / 1e9).ToString("0.#", provider)} Gbps"
            : $"{(bitsPerSecond / 1e6).ToString("0", provider)} Mbps";
    }
}