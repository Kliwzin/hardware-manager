using CoreWindows.Sensors;
using CoreWindows.Sensors.Nvidia;

namespace CoreWindows.Tests;

public class NvmlDataTests
{
    private static ComponentInfo Gpu(string id, string name) => new(id, name, ComponentKind.Gpu, null);

    [Fact]
    public void Sem_motivos_a_descricao_fica_vazia()
        => Assert.Empty(NvmlData.Describe(NvmlThrottleReasons.None));

    [Fact]
    public void Varios_motivos_ao_mesmo_tempo_viram_varios_textos()
    {
        var labels = NvmlData.Describe(NvmlThrottleReasons.SwPowerCap | NvmlThrottleReasons.HwThermalSlowdown);

        Assert.Equal(new[] { "limite de potência (software)", "limite térmico (hardware)" }, labels);
    }

    [Fact]
    public void Bit_desconhecido_aparece_em_vez_de_sumir()
    {
        var labels = NvmlData.Describe(NvmlThrottleReasons.GpuIdle | (NvmlThrottleReasons)0x1800);

        Assert.Equal(new[] { "GPU ociosa", "motivo desconhecido (0x1800)" }, labels);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060")]
    [InlineData("GeForce RTX 4060")]
    [InlineData("  nvidia   GeForce  rtx 4060 ")]
    public void Nome_normalizado_ignora_prefixo_caixa_e_espacos(string name)
        => Assert.Equal("GEFORCE RTX 4060", NvmlData.NormalizeName(name));

    [Fact]
    public void Texto_do_nvml_vai_ate_o_primeiro_zero()
    {
        byte[] buffer = [.. "560.94"u8, 0, (byte)'x', (byte)'y', 0, 0];

        Assert.Equal("560.94", NvmlData.TextFrom(buffer));
        Assert.Equal("", NvmlData.TextFrom(new byte[8]));
    }

    [Fact]
    public void Uma_gpu_e_ligada_pelo_nome_mesmo_escrito_diferente()
    {
        ComponentInfo[] components =
        [
            new("/intelcpu/0", "Intel Core i5-12400F", ComponentKind.Cpu, null),
            Gpu("/gpu-nvidia/0", "NVIDIA GeForce RTX 4060"),
        ];

        Assert.Equal(new[] { "/gpu-nvidia/0" }, NvmlData.Match(components, ["GeForce RTX 4060"]));
    }

    [Fact]
    public void Gpus_de_outras_marcas_nunca_sao_ligadas()
    {
        ComponentInfo[] components =
        [
            Gpu("/gpu-intel-integrated/0", "Intel UHD Graphics"),
            Gpu("/gpu-amd/0", "AMD Radeon RX 7600"),
        ];

        Assert.Equal(new string?[] { null }, NvmlData.Match(components, ["NVIDIA GeForce RTX 4060"]));
    }

    [Fact]
    public void Duas_placas_diferentes_sao_ligadas_pelo_nome_e_nao_pela_ordem()
    {
        ComponentInfo[] components =
        [
            Gpu("/gpu-nvidia/0", "NVIDIA GeForce RTX 4060"),
            Gpu("/gpu-nvidia/1", "NVIDIA GeForce GTX 1050"),
        ];

        var match = NvmlData.Match(components, ["NVIDIA GeForce GTX 1050", "NVIDIA GeForce RTX 4060"]);

        Assert.Equal(new[] { "/gpu-nvidia/1", "/gpu-nvidia/0" }, match);
    }

    [Fact]
    public void Duas_placas_iguais_sao_ligadas_na_ordem()
    {
        ComponentInfo[] components =
        [
            Gpu("/gpu-nvidia/0", "NVIDIA GeForce RTX 4060"),
            Gpu("/gpu-nvidia/1", "NVIDIA GeForce RTX 4060"),
        ];

        var match = NvmlData.Match(components, ["NVIDIA GeForce RTX 4060", "NVIDIA GeForce RTX 4060"]);

        Assert.Equal(new[] { "/gpu-nvidia/0", "/gpu-nvidia/1" }, match);
    }

    [Fact]
    public void Nome_que_nao_bate_e_ligado_pela_ordem_so_se_sobrar_a_mesma_quantidade()
    {
        ComponentInfo[] one = [Gpu("/gpu-nvidia/0", "NVIDIA RTX 4060")];
        ComponentInfo[] two = [Gpu("/gpu-nvidia/0", "NVIDIA RTX 4060"), Gpu("/gpu-nvidia/1", "NVIDIA GTX 1050")];

        Assert.Equal(new[] { "/gpu-nvidia/0" }, NvmlData.Match(one, ["Placa com outro nome"]));
        Assert.Equal(new string?[] { null }, NvmlData.Match(two, ["Placa com outro nome"])); // na dúvida, sem par
    }
}