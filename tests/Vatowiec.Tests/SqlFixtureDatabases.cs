namespace Vatowiec.Tests;

// Tymczasowe, puste bazy testów niszczących ([SqlServerFact]). Tworzy je i usuwa runner
// `DatabaseTools test`; prawdziwe bazy developerskie nie są przez te testy zmieniane.
internal static class SqlFixtureDatabases
{
    public const string System = "Vatowiec_System_Test";
    public const string CompanyA = "Vatowiec_Company_A_Test";
    public const string CompanyB = "Vatowiec_Company_B_Test";
}
