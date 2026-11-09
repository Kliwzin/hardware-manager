namespace CoreWindows.Inventory;

/// <summary>Traduz os códigos numéricos do Windows para nomes legíveis.</summary>
public static class InventoryNames
{
    // SMBIOSMemoryType (Win32_PhysicalMemory)
    public static string MemoryType(int code) => code switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        34 => "DDR5",
        29 => "LPDDR3",
        30 => "LPDDR4",
        35 => "LPDDR5",
        _ => "RAM"
    };

    // MediaType (MSFT_PhysicalDisk)
    public static string MediaType(int code) => code switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => "Disco"
    };

    // BusType (MSFT_PhysicalDisk)
    public static string BusType(int code) => code switch
    {
        1 => "SCSI",
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        10 => "SAS",
        11 => "SATA",
        17 => "NVMe",
        _ => "outro barramento"
    };

    // HealthStatus (MSFT_PhysicalDisk)
    public static string Health(int code) => code switch
    {
        0 => "Saudável",
        1 => "Atenção",
        2 => "Com problema",
        _ => "Desconhecida"
    };
}