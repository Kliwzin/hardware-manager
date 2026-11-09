namespace CoreWindows.Sensors;

/// <summary>Qualquer coisa capaz de ler sensores. Existe para podermos testar sem hardware real.</summary>
public interface ISensorSource : IDisposable
{
    IReadOnlyList<ComponentInfo> Components { get; }
    IReadOnlyList<SensorInfo> Sensors { get; }
    SensorSnapshot Read();
}