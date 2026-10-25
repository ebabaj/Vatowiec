using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Vatowiec.Core.Infrastructure.Personal;

// A newly installed LocalDB need not be on the PATH of an already running installer/client.
[SupportedOSPlatform("windows")]
public static class LocalDb2025Locator
{
    public static string FindExecutable()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var installed = machine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\17.0");
        if (installed?.GetValue("InstanceAPIPath") is string api && File.Exists(api) &&
            FileVersionInfo.GetVersionInfo(api).ProductMajorPart == 17)
        {
            var tools = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(api)!, "..", "..", "Tools", "Binn", "SqlLocalDB.exe"));
            if (File.Exists(tools) && FileVersionInfo.GetVersionInfo(tools).ProductMajorPart == 17) return tools;
        }
        throw new PersonalSetupException("Nie znaleziono SQL Server 2025 LocalDB (17.x). Uruchom ponownie instalator VATowiec Personal, aby przygotować zależność. Istniejące instancje i dane pozostają bez zmian.");
    }
}
