using CoreWindows.Sensors;
using CoreWindows.Storage;

/// <summary>
/// Modo "db": mostra o que o app gravou no banco de histórico. Não altera os dados; pode rodar com o app aberto.
/// Uso: dotnet run --project tools/SensorProbe -- db
/// </summary>
internal static class DbMode
{
    public static void Run()
    {
        string path = HistoryStore.PathFor("HardwareManager");
        Console.WriteLine($"Banco: {path}");

        if (!File.Exists(path))
        {
            Console.WriteLine("O arquivo ainda não existe. Abra o app e espere pelo menos 2 minutos.");
            return;
        }

        var store = new HistoryStore(path);
        var info = store.GetInfo();

        Console.WriteLine($"Tamanho: {info.FileBytes / 1024.0:0.#} KB");
        Console.WriteLine($"Sensores registrados: {info.Sensors}");
        Console.WriteLine($"Linhas por minuto: {info.MinuteRows}  |  linhas por hora: {info.HourRows}");

        if (info.FirstMinute is not { } first || info.LastMinute is not { } last)
        {
            Console.WriteLine("Nenhum minuto gravado ainda.");
            return;
        }

        int minutes = (int)(last - first).TotalMinutes + 1;
        Console.WriteLine($"Primeiro minuto: {first.LocalDateTime:dd/MM HH:mm}  |  último: {last.LocalDateTime:dd/MM HH:mm}  ({minutes} min de intervalo)");
        Console.WriteLine();

        // Até 4 sensores de temperatura, para conferir os valores contra o que o app mostra.
        var sample = store.ReadSensors().Where(s => s.Kind == SensorKind.Temperature).Take(4);
        var from = last.AddMinutes(-4);
        var to = last.AddMinutes(1);

        foreach (var sensor in sample)
        {
            Console.WriteLine($"{sensor.Name}  ({sensor.Id}) — últimos minutos:");
            foreach (var row in store.ReadMinutes(sensor.Id, from, to))
            {
                Console.WriteLine(
                    $"  {row.Start.LocalDateTime:HH:mm}" +
                    $"  média {SensorFormat.Format(sensor.Kind, row.Average),-8}" +
                    $"  mín {SensorFormat.Format(sensor.Kind, row.Min),-8}" +
                    $"  máx {SensorFormat.Format(sensor.Kind, row.Max),-8}" +
                    $"  {row.Samples} leituras");
            }

            var hours = store.ReadHours(sensor.Id, last.AddHours(-1), last.AddHours(1));
            if (hours.Count > 0)
            {
                var hour = hours[^1];
                Console.WriteLine(
                    $"  hora {hour.Start.LocalDateTime:HH}h: média {SensorFormat.Format(sensor.Kind, hour.Average)}, {hour.Samples} leituras");
            }
            Console.WriteLine();
        }
    }
}