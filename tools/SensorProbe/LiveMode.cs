using CoreWindows.Platform;
using CoreWindows.Sensors;

static class LiveMode
{
    public static async Task RunAsync()
    {
        Console.WriteLine($"Admin: {DriverStatus.IsAdministrator} | PawnIO: {DriverStatus.PawnIoVersion ?? "não instalado"}");

        await using var service = new SensorService(LhmSensorSource.CreateDefault);
        service.Start();
        await service.Ready;

        Console.WriteLine($"{service.Components.Count} componentes, {service.Sensors.Count} sensores:");
        foreach (var c in service.Components)
            Console.WriteLine($"  {c.Kind,-13} {c.Name}  ({c.Id})");
        Console.WriteLine();

        // Alguns sensores para acompanhar por 10 segundos.
        string[] wanted = ["CPU Package", "CPU Total", "GPU Core", "Composite Temperature"];
        var watched = service.Sensors.Where(s => wanted.Contains(s.Name)).ToList();

        service.SnapshotTaken += (_, snap) =>
        {
            var parts = watched
                .Where(s => snap.Values.ContainsKey(s.Id))
                .Select(s => $"{s.Name}: {SensorFormat.Format(s.Kind, snap.Values[s.Id])}");
            Console.WriteLine($"{snap.Timestamp:HH:mm:ss}  {string.Join("  |  ", parts)}");
        };

        await Task.Delay(TimeSpan.FromSeconds(10));

        // Resumo do histórico em memória (Fase 3A).
        var history = service.History;
        Console.WriteLine();
        Console.WriteLine($"Histórico: {history.Count} leituras guardadas (capacidade: {history.Capacity}).");
        foreach (var s in watched)
        {
            if (history.GetStats(s.Id) is not { } stats)
                continue;

            int points = history.GetSeries(s.Id).Count(p => p.Value is not null);
            Console.WriteLine(
                $"  {s.Name,-22} {s.Kind,-12} {points,3} pontos" +
                $" | mín {SensorFormat.Format(s.Kind, stats.Min)}" +
                $" | média {SensorFormat.Format(s.Kind, stats.Average)}" +
                $" | máx {SensorFormat.Format(s.Kind, stats.Max)}");
        }
    }
}