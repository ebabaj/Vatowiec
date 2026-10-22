namespace Vatowiec.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VATOWIEC_DATABASE_PER_COMPANY_SQL") != "1" ||
            Environment.GetEnvironmentVariable("VATOWIEC_DEMO_RESET_AUTHORIZED") != "1")
        {
            Skip = "Wymaga jawnego odtworzenia trzech baz demonstracyjnych przez DatabaseTools test; patrz docs/contractors.md. Test SQL nie został wykonany.";
        }
    }
}
