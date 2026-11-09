namespace CoreWindows.Sensors;

/// <summary>Um sensor em destaque no resumo de um componente.</summary>
public sealed record KeySensor(SensorInfo Sensor, string Label);

/// <summary>Escolhe os sensores principais de cada componente para a visão geral.</summary>
public static class KeySensors
{
    private sealed record Rule(SensorKind Kind, string Label, string[] Names);

    // Para cada regra, vale o primeiro nome encontrado (ordem de preferência).
    private static readonly Dictionary<ComponentKind, Rule[]> Rules = new()
    {
        [ComponentKind.Cpu] =
        [
            new(SensorKind.Load, "Uso", ["CPU Total"]),
            new(SensorKind.Temperature, "Temperatura", ["CPU Package"]),
            new(SensorKind.Power, "Consumo", ["CPU Package"]),
        ],
        [ComponentKind.Gpu] =
        [
            new(SensorKind.Load, "Uso", ["GPU Core"]),
            new(SensorKind.Temperature, "Temperatura", ["GPU Core"]),
            new(SensorKind.Clock, "Clock", ["GPU Core"]),
            new(SensorKind.SmallData, "VRAM usada", ["GPU Memory Used"]),
        ],
        [ComponentKind.Memory] =
        [
            new(SensorKind.Load, "Uso", ["Memory"]),
            new(SensorKind.Data, "Em uso", ["Memory Used"]),
            new(SensorKind.Data, "Disponível", ["Memory Available"]),
        ],
        [ComponentKind.Storage] =
        [
            new(SensorKind.Temperature, "Temperatura", ["Composite Temperature", "Temperature"]),
            new(SensorKind.Load, "Espaço usado", ["Used Space"]),
            new(SensorKind.Throughput, "Leitura", ["Read Rate"]),
            new(SensorKind.Throughput, "Escrita", ["Write Rate"]),
        ],
        [ComponentKind.Network] =
        [
            new(SensorKind.Throughput, "Download", ["Download Speed"]),
            new(SensorKind.Throughput, "Upload", ["Upload Speed"]),
        ],
    };

    public static IReadOnlyList<KeySensor> For(ComponentInfo component, IEnumerable<SensorInfo> sensors)
    {
        if (!Rules.TryGetValue(component.Kind, out var rules))
            return [];

        var own = sensors.Where(s => s.ComponentId == component.Id).ToList();
        var result = new List<KeySensor>();

        foreach (var rule in rules)
        {
            var match = rule.Names
                .Select(name => own.FirstOrDefault(s => s.Kind == rule.Kind && s.Name == name))
                .FirstOrDefault(s => s is not null);

            if (match is not null)
                result.Add(new KeySensor(match, rule.Label));
        }
        return result;
    }
}