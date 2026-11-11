namespace CoreWindows.Inventory;

public sealed record CpuInventory(
    string Name, string Manufacturer, int Cores, int Threads, int BaseClockMhz, string Socket);

public sealed record MemoryModule(
    string Slot, string Manufacturer, string PartNumber, ulong CapacityBytes,
    int SpeedMts, int ConfiguredSpeedMts, string Type);

public sealed record MemoryInventory(ulong TotalBytes, int TotalSlots, IReadOnlyList<MemoryModule> Modules)
{
    public int EmptySlots => Math.Max(0, TotalSlots - Modules.Count);
}

public sealed record BoardInventory(
    string Manufacturer, string Product, string BiosVendor, string BiosVersion, DateTime? BiosDate);

public sealed record VolumeInfo(string Letter, string Label, string FileSystem, long TotalBytes, long FreeBytes);

public sealed record DiskInventory(
    int Number, string Model, string MediaType, string BusType, ulong SizeBytes, string Health,
    IReadOnlyList<VolumeInfo> Volumes);

public sealed record NetworkAdapterInventory(
    string Name, string Description, string Type, long LinkSpeedBps, string Status, string Mac,
    IReadOnlyList<string> IPv4);

public sealed record DisplayMode(int Width, int Height, int RefreshHz);

public sealed record PhysicalSize(int WidthCm, int HeightCm)
{
    public double DiagonalInches => Math.Sqrt(WidthCm * WidthCm + HeightCm * HeightCm) / 2.54;
}

/// <summary>Size nulo = o EDID não informa. Mode nulo = conectado, mas fora da área de trabalho.</summary>
public sealed record MonitorInventory(
    string Name, string Manufacturer, string Serial, string Connection,
    PhysicalSize? Size, DisplayMode? Mode, bool IsPrimary);

/// <summary>Foto do sistema num instante. Seções nulas/vazias = não puderam ser lidas (ver Errors).</summary>
public sealed record SystemInventory(
    DateTimeOffset CollectedAt,
    CpuInventory? Cpu,
    MemoryInventory? Memory,
    BoardInventory? Board,
    IReadOnlyList<DiskInventory> Disks,
    IReadOnlyList<NetworkAdapterInventory> Network,
    IReadOnlyList<MonitorInventory> Monitors,
    IReadOnlyList<string> Errors);