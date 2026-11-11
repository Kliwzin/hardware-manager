using CoreWindows.Inventory;

namespace CoreWindows.Tests;

public class MonitorTests
{
    private const string WmiA = @"DISPLAY\GSM5B08\5&2a3b4c5d&0&UID4353_0";
    private const string WmiB = @"DISPLAY\GSM5B08\5&2a3b4c5d&0&UID4354_0";
    private const string PathA = @"\\?\DISPLAY#GSM5B08#5&2a3b4c5d&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string PathB = @"\\?\DISPLAY#GSM5B08#5&2a3b4c5d&0&UID4354#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static ushort[] Codes(string text, int size)
    {
        var codes = new ushort[size];
        for (int i = 0; i < text.Length; i++)
            codes[i] = text[i];
        return codes;
    }

    private static DisplayOutput Output(string path, int width = 1920, bool primary = false)
        => new(path, new DisplayMode(width, 1080, 60), primary);

    [Fact]
    public void Decodifica_texto_e_ignora_zeros_finais()
        => Assert.Equal("GSM", MonitorData.DecodeText(Codes("GSM", 16)));

    [Fact]
    public void Decodifica_texto_e_remove_espacos_e_quebra_de_linha()
        => Assert.Equal("LG ULTRAGEAR", MonitorData.DecodeText(Codes("LG ULTRAGEAR \n", 16)));

    [Fact]
    public void Texto_ausente_vira_vazio()
        => Assert.Equal("", MonitorData.DecodeText(null));

    [Theory]
    [InlineData("0", "")]
    [InlineData("00000000", "")]
    [InlineData("204NTKFAB123", "204NTKFAB123")]
    public void Serial_so_com_zeros_e_tratado_como_ausente(string raw, string expected)
        => Assert.Equal(expected, MonitorData.DecodeSerial(Codes(raw, 16)));

    [Fact]
    public void Wmi_e_caminho_do_dispositivo_geram_a_mesma_chave()
    {
        const string expected = @"DISPLAY\GSM5B08\5&2A3B4C5D&0&UID4353";
        Assert.Equal(expected, MonitorData.KeyFromWmi(WmiA));
        Assert.Equal(expected, MonitorData.KeyFromDevicePath(PathA));
    }

    [Fact]
    public void Dois_monitores_iguais_nao_se_misturam()
    {
        var monitors = MonitorData.Merge(
            [Output(PathA, width: 2560), Output(PathB, width: 1920)],
            [new(WmiB, "GSM", "5B08", "LG B", "SERIALB"), new(WmiA, "GSM", "5B08", "LG A", "SERIALA")],
            [new(WmiA, 60, 34), new(WmiB, 53, 30)],
            [new(WmiA, 10), new(WmiB, 5)]);

        Assert.Equal(2, monitors.Count);

        Assert.Equal("LG A", monitors[0].Name);
        Assert.Equal("SERIALA", monitors[0].Serial);
        Assert.Equal(2560, monitors[0].Mode!.Width);
        Assert.Equal("DisplayPort", monitors[0].Connection);
        Assert.Equal(60, monitors[0].Size!.WidthCm);

        Assert.Equal("LG B", monitors[1].Name);
        Assert.Equal("SERIALB", monitors[1].Serial);
        Assert.Equal(1920, monitors[1].Mode!.Width);
        Assert.Equal("HDMI", monitors[1].Connection);
        Assert.Equal(53, monitors[1].Size!.WidthCm);
    }

    [Fact]
    public void Monitor_principal_vem_primeiro()
    {
        var monitors = MonitorData.Merge(
            [Output(PathA), Output(PathB, primary: true)],
            [new(WmiA, "GSM", "5B08", "A", ""), new(WmiB, "GSM", "5B08", "B", "")], [], []);

        Assert.Equal(new[] { "B", "A" }, monitors.Select(m => m.Name));
    }

    [Fact]
    public void Sem_dados_do_wmi_o_monitor_ainda_aparece()
    {
        var monitor = Assert.Single(MonitorData.Merge([Output(PathA)], [], [], []));

        Assert.Equal("Monitor sem identificação", monitor.Name);
        Assert.Equal("Desconhecida", monitor.Connection);
        Assert.Null(monitor.Size);
        Assert.NotNull(monitor.Mode);
    }

    [Fact]
    public void Monitor_conectado_fora_da_area_de_trabalho_aparece_sem_modo()
    {
        var monitors = MonitorData.Merge(
            [Output(PathA)],
            [new(WmiA, "GSM", "5B08", "A", ""), new(WmiB, "DEL", "A0B1", "B", "")], [], []);

        Assert.Equal(2, monitors.Count);
        Assert.NotNull(monitors[0].Mode);
        Assert.Equal("B", monitors[1].Name);
        Assert.Null(monitors[1].Mode);
    }

    [Fact]
    public void Tela_sem_nome_no_edid_usa_fabricante_e_produto()
    {
        var monitor = Assert.Single(MonitorData.Merge(
            [Output(PathA)], [new(WmiA, "AUO", "B3FA", "", "")], [], []));

        Assert.Equal("AU Optronics B3FA", monitor.Name);
    }

    [Fact]
    public void Tamanho_zero_no_edid_vira_nulo()
    {
        var monitor = Assert.Single(MonitorData.Merge([Output(PathA)], [], [new(WmiA, 0, 0)], []));
        Assert.Null(monitor.Size);
    }

    [Theory]
    [InlineData(60, 34, 27.2)]
    [InlineData(53, 30, 24.0)]
    public void Calcula_diagonal_em_polegadas(int width, int height, double expected)
        => Assert.Equal(expected, new PhysicalSize(width, height).DiagonalInches, precision: 1);

    [Theory]
    [InlineData(0L, "VGA")]
    [InlineData(5L, "HDMI")]
    [InlineData(10L, "DisplayPort")]
    [InlineData(11L, "DisplayPort embutido (eDP)")]
    [InlineData(2147483648L, "Interna")]
    [InlineData(4294967295L, "Desconhecida")]
    public void Traduz_tipo_de_conexao(long code, string expected)
        => Assert.Equal(expected, InventoryNames.MonitorConnection(code));

    [Theory]
    [InlineData("GSM", "LG")]
    [InlineData("DEL", "Dell")]
    [InlineData("XYZ", "XYZ")]
    public void Traduz_fabricante_e_mantem_codigo_desconhecido(string code, string expected)
        => Assert.Equal(expected, InventoryNames.MonitorManufacturer(code));
}