using CoreWindows.Sensors.Nvidia;

namespace CoreWindows.Sensors;

/// <summary>
/// Junta várias fontes numa só. A primeira é a principal (o LHM): se ela falhar, a leitura falha.
/// As outras são extras: acrescentam sensores, nunca substituem os da principal, e uma falha delas
/// só faz os seus sensores ficarem sem valor naquela leitura.
/// </summary>
public sealed class CompositeSensorSource : ISensorSource
{
    private readonly ISensorSource _primary;
    private readonly (ISensorSource Source, HashSet<string> SensorIds)[] _extras;

    public CompositeSensorSource(ISensorSource primary, params ISensorSource[] extras)
    {
        _primary = primary;

        var components = primary.Components.ToList();
        var sensors = primary.Sensors.ToList();
        var componentIds = components.Select(c => c.Id).ToHashSet();
        var sensorIds = sensors.Select(s => s.Id).ToHashSet();

        _extras = new (ISensorSource, HashSet<string>)[extras.Length];
        for (int i = 0; i < extras.Length; i++)
        {
            foreach (var component in extras[i].Components)
            {
                if (componentIds.Add(component.Id))
                    components.Add(component);
            }

            // Id que já existe fica com quem chegou primeiro (mesma regra do TryAdd no SensorService).
            var own = new HashSet<string>();
            foreach (var sensor in extras[i].Sensors)
            {
                if (sensorIds.Add(sensor.Id))
                {
                    sensors.Add(sensor);
                    own.Add(sensor.Id);
                }
            }
            _extras[i] = (extras[i], own);
        }

        Components = components;
        Sensors = sensors;
        Sources = [primary, .. extras];
    }

    /// <summary>
    /// O LHM mais os extras que existirem neste PC (hoje: NVML, só com placa NVIDIA).
    /// Sem nenhum extra, devolve o próprio LHM, sem camada no meio.
    /// </summary>
    public static ISensorSource CreateDefault()
    {
        var lhm = LhmSensorSource.CreateDefault();
        var nvml = NvmlSensorSource.TryCreate(lhm.Components, lhm.Sensors);
        return nvml is null ? lhm : new CompositeSensorSource(lhm, nvml);
    }

    public IReadOnlyList<ComponentInfo> Components { get; }
    public IReadOnlyList<SensorInfo> Sensors { get; }

    /// <summary>As fontes juntadas, com a principal primeiro.</summary>
    public IReadOnlyList<ISensorSource> Sources { get; }

    /// <summary>Erro de uma fonte extra na última leitura; nulo se todas responderam.</summary>
    public Exception? LastExtraError { get; private set; }

    public SensorSnapshot Read()
    {
        var snapshot = _primary.Read();
        if (_extras.Length == 0)
            return snapshot;

        var values = new Dictionary<string, double>(snapshot.Values);
        Exception? error = null;

        foreach (var (source, sensorIds) in _extras)
        {
            try
            {
                foreach (var (id, value) in source.Read().Values)
                {
                    if (sensorIds.Contains(id))
                        values[id] = value;
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }

        LastExtraError = error;
        return new SensorSnapshot(snapshot.Timestamp, values);
    }

    public void Dispose()
    {
        // Ordem inversa da criação; uma fonte que falha ao fechar não impede as outras de fechar.
        for (int i = Sources.Count - 1; i >= 0; i--)
        {
            try { Sources[i].Dispose(); }
            catch (Exception) { }
        }
    }
}