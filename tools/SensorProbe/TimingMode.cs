using System.Diagnostics;
using CoreWindows.Sensors;

static class TimingMode
{
    public static void Run()
    {
        var sw = Stopwatch.StartNew();
        using var source = LhmSensorSource.CreateDefault();
        Console.WriteLine($"Abrir: {sw.ElapsedMilliseconds} ms");

        for (int i = 1; i <= 5; i++)
        {
            sw.Restart();
            source.Read();
            Console.WriteLine($"\nLeitura {i}: {sw.ElapsedMilliseconds} ms no total");

            foreach (var (id, time) in source.LastUpdateDurations.OrderByDescending(p => p.Value))
                Console.WriteLine($"  {time.TotalMilliseconds,8:0.0} ms  {id}");

            Thread.Sleep(1000);
        }
    }
}