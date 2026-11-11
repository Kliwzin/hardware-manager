namespace CoreWindows.Sensors;

/// <summary>Um sensor desenhado num gráfico, com o rótulo que aparece na legenda.</summary>
public sealed record ChartSensor(SensorInfo Sensor, string Label);

/// <summary>Um gráfico da tela de detalhe: sensores do mesmo tipo (mesma unidade) num eixo só.</summary>
public sealed record ChartGroup(string Title, SensorKind Kind, IReadOnlyList<ChartSensor> Sensors);

/// <summary>
/// Escolhe os gráficos da tela de detalhe de cada componente, por regra tipo + nome (como o KeySensors).
/// Só entram nomes de sensor já vistos nos relatórios de docs/sensores; gráfico sem sensor não aparece.
/// </summary>
public static class DetailCharts
{
    /// <summary>Máximo de linhas por gráfico: é o tamanho da paleta de cores. Acima disso, o resto fica de fora.</summary>
    public const int MaxSeries = 8;

    /// <summary>
    /// Uma linha do gráfico. Vale o primeiro nome encontrado (ordem de preferência).
    /// Nome terminado em '*' aceita vários sensores numerados ("GPU Fan *" → "GPU Fan 1", "GPU Fan 2");
    /// nesse caso, {0} no rótulo vira o número.
    /// </summary>
    private sealed record Line(string Label, params string[] Names);

    private sealed record Rule(string Title, SensorKind Kind, Line[] Lines);

    private static readonly Dictionary<ComponentKind, Rule[]> Rules = new()
    {
        [ComponentKind.Cpu] =
        [
            new("Utilização", SensorKind.Load,
                [new("Total", "CPU Total"),
                 new("Núcleo mais usado", "CPU Core Max")]),
            new("Temperatura", SensorKind.Temperature,
                [new("Pacote", "CPU Package"),
                 new("Núcleo mais quente", "Core Max"),
                 new("Média dos núcleos", "Core Average")]),
            new("Clock", SensorKind.Clock,
                [new("Núcleo {0}", "CPU Core #*")]),
            new("Consumo", SensorKind.Power,
                [new("Pacote", "CPU Package"),
                 new("Núcleos", "CPU Cores")]),
        ],
        [ComponentKind.Gpu] =
        [
            new("Utilização", SensorKind.Load,
                [new("GPU", "GPU Core"),
                 new("Controlador de memória", "GPU Memory Controller"),
                 new("Vídeo", "GPU Video Engine")]),
            new("Temperatura", SensorKind.Temperature,
                [new("GPU", "GPU Core"),
                 new("Ponto mais quente", "GPU Hot Spot")]),
            new("Clock", SensorKind.Clock,
                [new("GPU", "GPU Core"),
                 new("Memória", "GPU Memory")]),
            new("Consumo", SensorKind.Power,
                [new("GPU", "GPU Package")]),
            new("VRAM", SensorKind.SmallData,
                [new("Em uso", "GPU Memory Used")]),
            new("Ventoinhas", SensorKind.Fan,
                [new("Ventoinha {0}", "GPU Fan *")]),
        ],
        [ComponentKind.Memory] =
        [
            new("Utilização", SensorKind.Load,
                [new("Memória", "Memory")]),
            new("Memória", SensorKind.Data,
                [new("Em uso", "Memory Used"),
                 new("Disponível", "Memory Available")]),
        ],
        [ComponentKind.Storage] =
        [
            new("Temperatura", SensorKind.Temperature,
                [new("Temperatura", "Composite Temperature", "Temperature")]),
            new("Atividade", SensorKind.Load,
                [new("Total", "Total Activity")]),
            new("Transferência", SensorKind.Throughput,
                [new("Leitura", "Read Rate"),
                 new("Escrita", "Write Rate")]),
        ],
        [ComponentKind.Network] =
        [
            new("Tráfego", SensorKind.Throughput,
                [new("Download", "Download Speed"),
                 new("Upload", "Upload Speed")]),
        ],
    };

    public static IReadOnlyList<ChartGroup> For(ComponentInfo component, IEnumerable<SensorInfo> sensors)
    {
        if (!Rules.TryGetValue(component.Kind, out var rules))
            return [];

        var own = sensors.Where(s => s.ComponentId == component.Id).ToList();
        var result = new List<ChartGroup>();

        foreach (var rule in rules)
        {
            var ofKind = own.Where(s => s.Kind == rule.Kind).ToList();
            var chosen = rule.Lines.SelectMany(line => Match(line, ofKind)).Take(MaxSeries).ToList();

            if (chosen.Count > 0)
                result.Add(new ChartGroup(rule.Title, rule.Kind, chosen));
        }
        return result;
    }

    private static IEnumerable<ChartSensor> Match(Line line, List<SensorInfo> sensors)
    {
        foreach (string name in line.Names)
        {
            var found = name.EndsWith('*')
                ? MatchNumbered(line.Label, name[..^1], sensors)
                : sensors.Where(s => s.Name == name).Take(1).Select(s => new ChartSensor(s, line.Label)).ToList();

            if (found.Count > 0)
                return found;
        }
        return [];
    }

    /// <summary>Sensores cujo nome é o prefixo seguido só de dígitos ("CPU Core #3" sim, "CPU Core #3 Distance to TjMax" não).</summary>
    private static List<ChartSensor> MatchNumbered(string label, string prefix, List<SensorInfo> sensors)
    {
        var found = new List<ChartSensor>();
        foreach (var sensor in sensors)
        {
            if (!sensor.Name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            string number = sensor.Name[prefix.Length..];
            if (number.Length > 0 && number.All(char.IsAsciiDigit))
                found.Add(new ChartSensor(sensor, string.Format(label, number)));
        }
        return found;
    }
}
