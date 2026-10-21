using Vatowiec.DatabaseTools;

if (args.Length != 1 || args[0] is not ("inspect" or "initialize" or "prepare-tests" or "cleanup-tests" or "verify" or "test"))
{
    Console.Error.WriteLine("Użycie: inspect | initialize | prepare-tests | cleanup-tests | verify | test");
    return 2;
}

try
{
    await LocalDatabases.EnsureLocalServer();
    switch (args[0])
    {
        case "inspect": await LocalDatabases.Inspect(); break;
        case "initialize": await LocalDatabases.Initialize(); break;
        case "prepare-tests": await LocalDatabases.PrepareTests(); break;
        case "cleanup-tests": await LocalDatabases.CleanupTests(); break;
        case "verify": await LocalDatabases.Verify(); break;
        case "test": await LocalDatabases.Test(); break;
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Operacja przerwana: {error.Message}");
    return 1;
}
