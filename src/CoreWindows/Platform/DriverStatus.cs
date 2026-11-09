using System.Security.Principal;
using Microsoft.Win32;

namespace CoreWindows.Platform;

/// <summary>Informações do ambiente que mudam o que o app consegue ler.</summary>
public static class DriverStatus
{
    public static bool IsAdministrator =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static string? PawnIoVersion =>
        Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO",
            "DisplayVersion", null) as string;

    public static bool IsPawnIoInstalled => PawnIoVersion is not null;
}