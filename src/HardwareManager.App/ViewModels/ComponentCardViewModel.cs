using CoreWindows.Sensors;

namespace HardwareManager.App.ViewModels;

public sealed class ComponentCardViewModel(ComponentInfo component, IReadOnlyList<SensorValueViewModel> values)
{
    public string Title { get; } = component.Name;

    public string KindLabel { get; } = component.Kind switch
    {
        ComponentKind.Cpu => "Processador",
        ComponentKind.Gpu => "Placa de vídeo",
        ComponentKind.Memory => "Memória RAM",
        ComponentKind.Storage => "Armazenamento",
        ComponentKind.Network => "Rede",
        _ => ""
    };

    public IReadOnlyList<SensorValueViewModel> Values { get; } = values;
}