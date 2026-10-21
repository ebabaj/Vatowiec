namespace Vatowiec.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VATOWIEC_SQL_INTEGRATION") != "1")
        {
            Skip = "Włącz VATOWIEC_SQL_INTEGRATION=1 i skonfiguruj lokalną Vatowiec_Dev (README).";
        }
    }
}
