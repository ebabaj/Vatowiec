namespace Vatowiec.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VATOWIEC_DATABASE_PER_COMPANY_SQL") != "1" ||
            Environment.GetEnvironmentVariable("VATOWIEC_DEMO_RESET_AUTHORIZED") != "1")
        {
            Skip = "Wymaga tymczasowych baz testowych tworzonych przez DatabaseTools test (docs/database-per-company.md). Test SQL nie został wykonany.";
        }
    }
}
