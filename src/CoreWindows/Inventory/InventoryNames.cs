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

    // VideoOutputTechnology (WmiMonitorConnectionParams).
    // long porque "Interna" é 0x80000000 e os valores negativos da documentação chegam como uint32.
    public static string MonitorConnection(long code) => code switch
    {
        0 => "VGA",
        1 => "S-Video",
        2 => "Vídeo composto",
        3 => "Vídeo componente",
        4 => "DVI",
        5 => "HDMI",
        6 => "Interna (LVDS)",
        9 => "SDI",
        10 => "DisplayPort",
        11 => "DisplayPort embutido (eDP)",
        15 => "Miracast (sem fio)",
        16 => "Indireta com fio (ex.: USB)",
        0x80000000 => "Interna",
        _ => "Desconhecida"
    };

    // Código PnP de 3 letras do EDID. Lista só com as marcas comuns; o resto mostra o próprio código.
    public static string MonitorManufacturer(string code) => code switch
    {
        "ACI" or "AUS" => "ASUS",
        "ACR" => "Acer",
        "AOC" => "AOC",
        "APP" => "Apple",
        "AUO" => "AU Optronics",
        "BNQ" => "BenQ",
        "BOE" => "BOE",
        "CMN" => "Innolux",
        "DEL" => "Dell",
        "ENC" => "EIZO",
        "GBT" => "Gigabyte",
        "GSM" => "LG",
        "HPN" or "HWP" => "HP",
        "IVM" => "iiyama",
        "LEN" => "Lenovo",
        "LGD" => "LG Display",
        "MSI" => "MSI",
        "PHL" => "Philips",
        "SAM" => "Samsung",
        "SDC" => "Samsung Display",
        "SHP" => "Sharp",
        "SNY" => "Sony",
        "VSC" => "ViewSonic",
        _ => code
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