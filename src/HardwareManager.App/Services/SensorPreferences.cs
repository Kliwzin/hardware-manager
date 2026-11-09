using System.IO;
using System.Text.Json;

namespace HardwareManager.App.Services;

/// <summary>Nomes personalizados e sensores ocultos, salvos em %LocalAppData%\HardwareManager\sensors.json.</summary>
public sealed class SensorPreferences
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HardwareManager", "sensors.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly PreferencesData _data = Load();

    /// <summary>Disparado depois de qualquer alteração.</summary>
    public event Action? Changed;

    public string? GetCustomName(string sensorId) => _data.Names.GetValueOrDefault(sensorId);

    public bool IsHidden(string sensorId) => _data.Hidden.Contains(sensorId);

    public void SetCustomName(string sensorId, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            _data.Names.Remove(sensorId);
        else
            _data.Names[sensorId] = name.Trim();
        Save();
    }

    public void SetHidden(string sensorId, bool hidden)
    {
        if (hidden)
            _data.Hidden.Add(sensorId);
        else
            _data.Hidden.Remove(sensorId);
        Save();
    }

    private static PreferencesData Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<PreferencesData>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Arquivo corrompido ou inacessível: começa do zero em vez de quebrar o app.
        }
        return new();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, JsonOptions));
        Changed?.Invoke();
    }

    public sealed class PreferencesData
    {
        public Dictionary<string, string> Names { get; set; } = new();
        public HashSet<string> Hidden { get; set; } = new();
    }
}