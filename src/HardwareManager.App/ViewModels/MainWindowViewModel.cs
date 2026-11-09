using CommunityToolkit.Mvvm.ComponentModel;
using CoreWindows.Platform;
using HardwareManager.App.Services;
using Wpf.Ui.Controls;

namespace HardwareManager.App.ViewModels;

public sealed partial class MainWindowViewModel(
    UiSensorFeed feed, OverviewViewModel overview, SensorsViewModel sensors) : ObservableObject
{
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _showWarning;
    [ObservableProperty] private string _warningTitle = "";
    [ObservableProperty] private string _warningMessage = "";
    [ObservableProperty] private InfoBarSeverity _warningSeverity = InfoBarSeverity.Warning;

    public async Task InitializeAsync()
    {
        if (!DriverStatus.IsAdministrator)
        {
            Warn(InfoBarSeverity.Warning, "Sem permissão de administrador",
                "Vários sensores só podem ser lidos como administrador. Feche e abra o app com \"Executar como administrador\".");
        }
        else if (!DriverStatus.IsPawnIoInstalled)
        {
            Warn(InfoBarSeverity.Warning, "Modo limitado: driver PawnIO não instalado",
                "Sem ele não aparecem temperatura, clock e consumo da CPU, os sensores da placa-mãe " +
                "(tensões, temperaturas, ventoinhas) e os dados dos módulos de RAM. " +
                "Para instalar: winget install namazso.PawnIO — depois reabra o app.");
        }

        try
        {
            await feed.Sensors.Ready; // espera o LHM abrir e fazer a 1ª leitura
            overview.Build();
            sensors.Build();
        }
        catch (Exception ex)
        {
            Warn(InfoBarSeverity.Error, "Não foi possível ler os sensores", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Warn(InfoBarSeverity severity, string title, string message)
    {
        WarningSeverity = severity;
        WarningTitle = title;
        WarningMessage = message;
        ShowWarning = true;
    }
}