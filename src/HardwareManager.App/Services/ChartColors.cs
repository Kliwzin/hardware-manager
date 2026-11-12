using SkiaSharp;
using Wpf.Ui.Appearance;

namespace HardwareManager.App.Services;

/// <summary>
/// Cores dos gráficos para um tema. As oito cores de série são as mesmas nos dois temas,
/// em tons ajustados para o fundo claro ou escuro; a ordem é fixa (a 1ª linha é sempre azul...).
/// Surface é o fundo da área do gráfico: sem ele, o LiveCharts pinta o fundo de branco em qualquer tema.
/// </summary>
public sealed record ChartColors(IReadOnlyList<SKColor> Series, SKColor Surface, SKColor AxisText, SKColor GridLine)
{
    private static readonly ChartColors Light = new(
        Parse("#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"),
        Surface: SKColor.Parse("#fcfcfb"),
        AxisText: SKColor.Parse("#52514e"),
        GridLine: new SKColor(0, 0, 0, 28));

    private static readonly ChartColors Dark = new(
        Parse("#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"),
        Surface: SKColor.Parse("#1a1a19"),
        AxisText: SKColor.Parse("#c3c2b7"),
        GridLine: new SKColor(255, 255, 255, 36));

    /// <summary>Cores para o tema em que o app está agora.</summary>
    public static ChartColors Current
        => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark ? Dark : Light;

    private static SKColor[] Parse(params string[] hex) => [.. hex.Select(SKColor.Parse)];
}