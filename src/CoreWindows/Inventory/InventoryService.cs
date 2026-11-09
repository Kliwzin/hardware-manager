using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CoreWindows.Sensors;

namespace CoreWindows.Inventory;

/// <summary>Coleta o inventário do sistema (WMI + APIs do .NET). Leva de 1 a 3 s.</summary>
public static class InventoryService
{
    public static Task<SystemInventory> CollectAsync() => Task.Run(Collect);

    public static SystemInventory Collect()
    {
        var errors = new List<string>();

        // Cada seção é lida isoladamente: se uma falhar, as outras continuam.
        T? Try<T>(string section, Func<T> read) where T : class
        {
            try { return read(); }
            catch (Exception ex)
            {
                errors.Add($"{section}: {ex.Message}");
                return null;
            }
        }

        var cpu = Try("Processador", ReadCpu);
        var memory = Try("Memória", ReadMemory);
        var board = Try("Placa-mãe", ReadBoard);
        var disks = Try("Discos", ReadDisks) ?? [];
        var network = Try("Rede", ReadNetwork) ?? [];

        return new SystemInventory(DateTimeOffset.Now, cpu, memory, board, disks, network, errors);
    }

    private static CpuInventory ReadCpu() =>
        Wmi.Query(
            "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, SocketDesignation FROM Win32_Processor",
            o => new CpuInventory(
                Wmi.Text(o, "Name"),
                Wmi.Text(o, "Manufacturer"),
                (int)Wmi.Number(o, "NumberOfCores"),
                (int)Wmi.Number(o, "NumberOfLogicalProcessors"),
                (int)Wmi.Number(o, "MaxClockSpeed"),
                Wmi.Text(o, "SocketDesignation")))
        .FirstOrDefault()
        ?? throw new InvalidOperationException("Nenhum processador encontrado no WMI.");

    private static MemoryInventory ReadMemory()
    {
        var modules = Wmi.Query(
            "SELECT DeviceLocator, Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory",
            o => new MemoryModule(
                Wmi.Text(o, "DeviceLocator"),
                Wmi.Text(o, "Manufacturer"),
                Wmi.Text(o, "PartNumber"),
                Wmi.UNumber(o, "Capacity"),
                (int)Wmi.Number(o, "Speed"),
                (int)Wmi.Number(o, "ConfiguredClockSpeed"),
                InventoryNames.MemoryType((int)Wmi.Number(o, "SMBIOSMemoryType"))));

        // Use = 3 → memória do sistema (exclui ROM, cache etc.)
        int slots = (int)Wmi.Query(
            "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray WHERE Use = 3",
            o => Wmi.Number(o, "MemoryDevices")).Sum();

        ulong total = modules.Aggregate(0UL, (sum, m) => sum + m.CapacityBytes);
        return new MemoryInventory(total, slots, modules);
    }

    private static BoardInventory ReadBoard()
    {
        var (manufacturer, product) = Wmi.Query(
            "SELECT Manufacturer, Product FROM Win32_BaseBoard",
            o => (Wmi.Text(o, "Manufacturer"), Wmi.Text(o, "Product"))).FirstOrDefault();

        var (vendor, version, date) = Wmi.Query(
            "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS",
            o => (Wmi.Text(o, "Manufacturer"), Wmi.Text(o, "SMBIOSBIOSVersion"), Wmi.Text(o, "ReleaseDate"))).FirstOrDefault();

        // A data vem no formato CIM: "20230815000000.000000+000"
        DateTime? biosDate = string.IsNullOrEmpty(date) ? null : ManagementDateTimeConverter.ToDateTime(date);

        return new BoardInventory(manufacturer ?? "", product ?? "", vendor ?? "", version ?? "", biosDate);
    }

    private static IReadOnlyList<DiskInventory> ReadDisks()
    {
        // Partições com letra → agrupadas por número do disco.
        var volumesByDisk = Wmi.Query(
                "SELECT DiskNumber, DriveLetter FROM MSFT_Partition",
                o => (Disk: (int)Wmi.Number(o, "DiskNumber"), Letter: Convert.ToChar(o["DriveLetter"] ?? '\0')),
                Wmi.Storage)
            .Where(p => char.IsLetter(p.Letter))
            .GroupBy(p => p.Disk)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<VolumeInfo>)g.Select(p => ReadVolume(p.Letter)).OfType<VolumeInfo>().ToList());

        return Wmi.Query(
                "SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk",
                o =>
                {
                    int number = int.TryParse(Wmi.Text(o, "DeviceId"), out int n) ? n : -1;
                    return new DiskInventory(
                        number,
                        Wmi.Text(o, "FriendlyName"),
                        InventoryNames.MediaType((int)Wmi.Number(o, "MediaType")),
                        InventoryNames.BusType((int)Wmi.Number(o, "BusType")),
                        Wmi.UNumber(o, "Size"),
                        InventoryNames.Health((int)Wmi.Number(o, "HealthStatus")),
                        volumesByDisk.GetValueOrDefault(number, []));
                },
                Wmi.Storage)
            .OrderBy(d => d.Number)
            .ToList();
    }

    private static VolumeInfo? ReadVolume(char letter)
    {
        var drive = new DriveInfo($"{letter}:\\");
        if (!drive.IsReady)
            return null;

        return new VolumeInfo($"{letter}:", drive.VolumeLabel, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace);
    }

    private static IReadOnlyList<NetworkAdapterInventory> ReadNetwork()
    {
        // Mesmo critério dos sensores: só adaptadores físicos.
        var physical = PhysicalNetworkAdapters.GetGuids();

        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => physical.Contains(ni.Id))
            .Select(ni => new NetworkAdapterInventory(
                ni.Name,
                ni.Description,
                ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Ethernet => "Cabo (Ethernet)",
                    NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                    var other => other.ToString()
                },
                ni.Speed,
                ni.OperationalStatus == OperationalStatus.Up ? "Conectado" : "Desconectado",
                string.Join(":", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))),
                ni.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .ToList()))
            .ToList();
    }
}