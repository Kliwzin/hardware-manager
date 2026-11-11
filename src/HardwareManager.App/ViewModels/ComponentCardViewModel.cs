using CoreWindows.Sensors;

namespace HardwareManager.App.ViewModels;

public sealed class ComponentCardViewModel(ComponentInfo component, IReadOnlyList<SensorValueViewModel> values)
{
    public ComponentInfo Component { get; } = component;

    public string Title { get; } = component.Name;

    public string KindLabel { get; } = LabelFor(component.Kind);

    public IReadOnlyList<SensorValueViewModel> Values { get; } = values;

    public static string LabelFor(ComponentKind kind) => kind switch
    {
        ComponentKind.Cpu => "Processador",
        ComponentKind.Gpu => "Placa de vídeo",
        ComponentKind.Memory => "Memória RAM",
        ComponentKind.Storage => "Armazenamento",
        ComponentKind.Network => "Rede",
        _ => ""
    };
}