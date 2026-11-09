namespace HardwareManager.App.ViewModels;

public sealed record InfoLine(string Label, string Value);

public sealed record InfoSection(string Kind, string Title, IReadOnlyList<InfoLine> Lines);