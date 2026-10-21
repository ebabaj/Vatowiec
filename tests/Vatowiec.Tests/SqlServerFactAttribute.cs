namespace Vatowiec.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VATOWIEC_DATABASE_PER_COMPANY_SQL") != "1")
        {
            Skip = "Wymaga osobnych Vatowiec_System_Test i Vatowiec_Company_A_Test/B_Test po migracjach; patrz README. Nie wykonano na nowej strukturze baz.";
        }
    }
}
