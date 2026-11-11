namespace CoreWindows.Sensors;

/// <summary>
/// Garante que cada sensor de um componente tenha um id só dele.
/// O LHM repete identificadores em alguns casos: na GPU NVIDIA, "GPU Bus" e "GPU Memory"
/// (os dois de carga) saem como /gpu-nvidia/0/load/3.
/// </summary>
public static class SensorIds
{
    /// <summary>
    /// Devolve um id por sensor, na mesma ordem da entrada.
    /// Id que aparece uma vez só fica igual. Id repetido ganha o nome do sensor no fim
    /// ("/gpu-nvidia/0/load/3:gpu-bus"), em todos os repetidos — assim o resultado não depende
    /// da ordem em que o LHM lista os sensores. Se ainda sobrar repetição (mesmo id e mesmo nome),
    /// entra um número: "-2", "-3"...
    /// </summary>
    public static IReadOnlyList<string> MakeUnique(IReadOnlyList<(string Id, string Name)> sensors)
    {
        var repeated = sensors
            .GroupBy(s => s.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        var used = new HashSet<string>();
        var result = new string[sensors.Count];

        for (int i = 0; i < sensors.Count; i++)
        {
            var (id, name) = sensors[i];
            string candidate = repeated.Contains(id) ? $"{id}:{Slug(name)}" : id;

            string unique = candidate;
            for (int n = 2; !used.Add(unique); n++)
                unique = $"{candidate}-{n}";

            result[i] = unique;
        }
        return result;
    }

    /// <summary>"GPU Memory" → "gpu-memory".</summary>
    private static string Slug(string name)
        => string.Join('-', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
