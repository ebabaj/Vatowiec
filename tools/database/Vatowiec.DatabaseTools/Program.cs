using Vatowiec.DatabaseTools;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[0] is not ("verify-users" or "grant-installation-admin")) ||
    (args[0] == "grant-installation-admin" && args.Length != 2) || args[0] is not ("inspect" or "initialize" or "prepare-tests" or "cleanup-tests" or "verify" or "test" or "seed-users" or "backup-system" or "migrate-system" or "migrate-contractors" or "verify-users" or "probe-registries" or "grant-installation-admin"))
{
    Console.Error.WriteLine("Użycie: inspect | initialize | prepare-tests | cleanup-tests | verify | test | seed-users | backup-system | migrate-system | migrate-contractors | verify-users [https://localhost:7243] | grant-installation-admin <login>");
    return 2;
}

try
{
    await LocalDatabases.EnsureLocalServer();
    switch (args[0])
    {
        case "probe-registries": await RegistryProbe.Run(); break;
        case "inspect": await LocalDatabases.Inspect(); break;
        case "initialize": await LocalDatabases.Initialize(); break;
        case "prepare-tests": await LocalDatabases.PrepareTests(); break;
        case "cleanup-tests": await LocalDatabases.CleanupTests(); break;
        case "verify": await LocalDatabases.Verify(); break;
        case "test": await LocalDatabases.Test(); break;
        case "seed-users": await LocalDatabases.SeedUsers(); break;
        case "backup-system": await LocalDatabases.BackupSystem(); break;
        case "grant-installation-admin": await LocalDatabases.GrantInstallationAdministrator(args[1]); break;
        case "migrate-system": await LocalDatabases.MigrateSystem(); break;
        case "migrate-contractors": await LocalDatabases.MigrateContractors(); break;
        case "verify-users": await DemoAuthenticationProbe.Run(args.Length == 2 ? args[1] : "https://localhost:7243"); break;
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Operacja przerwana: {error.Message}");
    return 1;
}
