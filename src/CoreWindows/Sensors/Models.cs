namespace CoreWindows.Sensors;

/// <summary>Um componente de hardware. Id = identificador estável do LHM, ex.: "/gpu-nvidia/0".</summary>
public sealed record ComponentInfo(string Id, string Name, ComponentKind Kind, string? ParentId);

/// <summary>Dados fixos de um sensor. Id = ex.: "/intelcpu/0/temperature/6".</summary>
public sealed record SensorInfo(string Id, string ComponentId, string Name, SensorKind Kind);

/// <summary>Leitura de todos os sensores num instante. Sensores sem valor válido ficam de fora.</summary>
public sealed record SensorSnapshot(DateTimeOffset Timestamp, IReadOnlyDictionary<string, double> Values);