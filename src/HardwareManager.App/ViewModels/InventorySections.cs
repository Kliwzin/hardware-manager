using CoreWindows.Inventory;

namespace HardwareManager.App.ViewModels;

public static class InventorySections
{
    public static IEnumerable<InfoSection> Build(SystemInventory inv)
    {
        if (inv.Cpu is { } cpu)
        {
            yield return new InfoSection("Processador", cpu.Name,
            [
                new("Fabricante", cpu.Manufacturer),
                new("Núcleos / threads", $"{cpu.Cores} / {cpu.Threads}"),
                new("Clock base", $"{cpu.BaseClockMhz / 1000.0:0.0#} GHz"),
                new("Soquete", cpu.Socket),
            ]);
        }

        if (inv.Board is { } board)
        {
            yield return new InfoSection("Placa-mãe", board.Product,
            [
                new("Fabricante", board.Manufacturer),
                new("BIOS", $"{board.BiosVendor} {board.BiosVersion}".Trim()),
                new("Data da BIOS", board.BiosDate?.ToString("d") ?? "—"),
            ]);
        }

        if (inv.Memory is { } mem)
        {
            var lines = new List<InfoLine>
            {
                new("Total", InventoryFormat.Bytes(mem.TotalBytes)),
                new("Slots", mem.TotalSlots > 0
                    ? $"{mem.Modules.Count} em uso de {mem.TotalSlots} ({mem.EmptySlots} livre(s))"
                    : $"{mem.Modules.Count} em uso"),
            };
            foreach (var module in mem.Modules)
                lines.Add(new(module.Slot, DescribeModule(module)));

            string type = mem.Modules.FirstOrDefault()?.Type ?? "";
            yield return new InfoSection("Memória RAM", $"{InventoryFormat.Bytes(mem.TotalBytes)} {type}".Trim(), lines);
        }

        foreach (var disk in inv.Disks)
        {
            var lines = new List<InfoLine>
            {
                new("Tipo", $"{disk.MediaType} ({disk.BusType})"),
                new("Capacidade", InventoryFormat.Bytes(disk.SizeBytes, binary: false)),
                new("Saúde (Windows)", disk.Health),
            };
            foreach (var v in disk.Volumes)
            {
                string name = string.IsNullOrEmpty(v.Label) ? v.Letter : $"{v.Letter} ({v.Label})";
                lines.Add(new(name,
                    $"{InventoryFormat.Bytes(v.FreeBytes)} livres de {InventoryFormat.Bytes(v.TotalBytes)} · {v.FileSystem}"));
            }
            yield return new InfoSection("Armazenamento", disk.Model, lines);
        }

        foreach (var nic in inv.Network)
        {
            yield return new InfoSection("Rede", nic.Name,
            [
                new("Adaptador", nic.Description),
                new("Tipo", nic.Type),
                new("Estado", nic.Status),
                new("Velocidade do link", InventoryFormat.LinkSpeed(nic.LinkSpeedBps)),
                new("IPv4", nic.IPv4.Count > 0 ? string.Join(", ", nic.IPv4) : "—"),
                new("MAC", nic.Mac),
            ]);
        }

        foreach (var monitor in inv.Monitors)
        {
            yield return new InfoSection("Monitor", monitor.Name,
            [
                new("Fabricante", OrDash(monitor.Manufacturer)),
                new("Resolução", monitor.Mode is { } mode ? $"{mode.Width} × {mode.Height}" : "—"),
                new("Taxa de atualização", monitor.Mode is { RefreshHz: > 1 } rate ? $"{rate.RefreshHz} Hz" : "—"),
                new("Conexão", monitor.Connection),
                new("Tamanho", monitor.Size is { } size
                    ? $"≈ {size.DiagonalInches:0.#}\" ({size.WidthCm} × {size.HeightCm} cm)"
                    : "—"),
                new("Nº de série", OrDash(monitor.Serial)),
                new("Uso", DescribeUsage(monitor)),
            ]);
        }

        if (inv.Errors.Count > 0)
        {
            yield return new InfoSection("Avisos", "Partes que não puderam ser lidas",
                inv.Errors.Select(e => new InfoLine("", e)).ToList());
        }
    }

    private static string OrDash(string text) => string.IsNullOrEmpty(text) ? "—" : text;

    private static string DescribeUsage(MonitorInventory monitor)
    {
        if (monitor.Mode is null)
            return "Conectado, fora da área de trabalho";

        return monitor.IsPrimary ? "Monitor principal" : "Monitor secundário";
    }

    private static string DescribeModule(MemoryModule m)
    {
        string text = $"{InventoryFormat.Bytes(m.CapacityBytes)} {m.Type}-{m.SpeedMts} · {m.Manufacturer} {m.PartNumber}".Trim();

        // A memória suporta uma velocidade, mas está rodando abaixo dela.
        if (m.ConfiguredSpeedMts > 0 && m.ConfiguredSpeedMts < m.SpeedMts)
            text += $"\nRodando a {m.ConfiguredSpeedMts} MT/s (perfil XMP/DOCP desativado na BIOS?)";

        return text;
    }
}