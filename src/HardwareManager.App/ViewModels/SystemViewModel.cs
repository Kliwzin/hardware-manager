using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreWindows.Inventory;

namespace HardwareManager.App.ViewModels;

public sealed partial class SystemViewModel : ObservableObject
{
    public ObservableCollection<InfoSection> Sections { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = "";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        Status = "Coletando informações...";
        try
        {
            // O await volta para a thread da interface (aqui NÃO usamos ConfigureAwait(false)),
            // então podemos mexer em Sections logo em seguida.
            var inventory = await InventoryService.CollectAsync();

            Sections.Clear();
            foreach (var section in InventorySections.Build(inventory))
                Sections.Add(section);

            Status = $"Atualizado às {inventory.CollectedAt:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao coletar: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}