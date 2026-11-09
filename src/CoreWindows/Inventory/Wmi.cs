using System.Management;

namespace CoreWindows.Inventory;

/// <summary>Atalhos para consultas WMI que já liberam os objetos COM.</summary>
internal static class Wmi
{
    public const string Cimv2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";

    public static List<T> Query<T>(string query, Func<ManagementBaseObject, T> map, string scope = Cimv2)
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();

        var list = new List<T>();
        foreach (ManagementBaseObject item in results)
        {
            using (item)
                list.Add(map(item));
        }
        return list;
    }

    public static string Text(ManagementBaseObject o, string property) =>
        o[property]?.ToString()?.Trim() ?? "";

    // O WMI devolve números como UInt16, UInt32 ou UInt64 conforme a propriedade.
    // Convert aceita qualquer um deles, então não precisamos decorar o tipo de cada campo.
    public static long Number(ManagementBaseObject o, string property) =>
        o[property] is { } v ? Convert.ToInt64(v) : 0;

    public static ulong UNumber(ManagementBaseObject o, string property) =>
        o[property] is { } v ? Convert.ToUInt64(v) : 0;
}