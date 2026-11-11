using CoreWindows.Sensors;

namespace CoreWindows.Tests;

public class SensorIdsTests
{
    [Fact]
    public void Ids_sem_repeticao_ficam_iguais()
    {
        var ids = SensorIds.MakeUnique(
        [
            ("/gpu-nvidia/0/load/0", "GPU Core"),
            ("/gpu-nvidia/0/temperature/0", "GPU Core"),
        ]);

        Assert.Equal(new[] { "/gpu-nvidia/0/load/0", "/gpu-nvidia/0/temperature/0" }, ids);
    }

    [Fact]
    public void Id_repetido_ganha_o_nome_do_sensor_em_todos_os_repetidos()
    {
        // Caso real do PC 1 (LHM 0.9.6, RTX 4060).
        var ids = SensorIds.MakeUnique(
        [
            ("/gpu-nvidia/0/load/2", "GPU Video Engine"),
            ("/gpu-nvidia/0/load/3", "GPU Bus"),
            ("/gpu-nvidia/0/load/3", "GPU Memory"),
        ]);

        Assert.Equal(
            new[] { "/gpu-nvidia/0/load/2", "/gpu-nvidia/0/load/3:gpu-bus", "/gpu-nvidia/0/load/3:gpu-memory" },
            ids);
    }

    [Fact]
    public void Resultado_nao_depende_da_ordem_dos_sensores()
    {
        var ids = SensorIds.MakeUnique(
        [
            ("/gpu-nvidia/0/load/3", "GPU Memory"),
            ("/gpu-nvidia/0/load/3", "GPU Bus"),
        ]);

        Assert.Equal(new[] { "/gpu-nvidia/0/load/3:gpu-memory", "/gpu-nvidia/0/load/3:gpu-bus" }, ids);
    }

    [Fact]
    public void Mesmo_id_e_mesmo_nome_ganham_numero()
    {
        var ids = SensorIds.MakeUnique(
        [
            ("/x/0/fan/1", "Fan"),
            ("/x/0/fan/1", "Fan"),
            ("/x/0/fan/1", "Fan"),
        ]);

        Assert.Equal(new[] { "/x/0/fan/1:fan", "/x/0/fan/1:fan-2", "/x/0/fan/1:fan-3" }, ids);
        Assert.Equal(3, ids.Distinct().Count());
    }
}
