using System.Globalization;
using CoreWindows.Inventory;

namespace CoreWindows.Tests;

public class InventoryTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(512.0, true, "512 B")]
    [InlineData(17179869184.0, true, "16 GB")]        // módulo de 16 GB
    [InlineData(4000787030016.0, false, "4 TB")]      // seu HD, como o fabricante anuncia
    [InlineData(1024209543168.0, false, "1 TB")]      // seu NVMe
    public void Formata_bytes(double bytes, bool binary, string expected)
        => Assert.Equal(expected, InventoryFormat.Bytes(bytes, binary, Inv));

    [Theory]
    [InlineData(1_000_000_000L, "1 Gbps")]
    [InlineData(2_500_000_000L, "2.5 Gbps")]
    [InlineData(100_000_000L, "100 Mbps")]
    [InlineData(-1L, "—")]
    public void Formata_velocidade_de_link(long bps, string expected)
        => Assert.Equal(expected, InventoryFormat.LinkSpeed(bps, Inv));

    [Theory]
    [InlineData(26, "DDR4")]
    [InlineData(34, "DDR5")]
    public void Traduz_tipo_de_memoria(int code, string expected)
        => Assert.Equal(expected, InventoryNames.MemoryType(code));

    [Fact]
    public void Traduz_disco_nvme()
    {
        Assert.Equal("NVMe", InventoryNames.BusType(17));
        Assert.Equal("SSD", InventoryNames.MediaType(4));
    }
}