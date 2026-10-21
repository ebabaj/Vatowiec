namespace Vatowiec.Core.Infrastructure.Persistence;

// Obiekt tylko dla serwera. Przyszły resolver tworzy go w scope PO autoryzacji;
// nie może być bindowany z żądania HTTP ani trafiać do publicznych kontraktów.
public sealed class CompanyDatabaseSelection
{
    public CompanyDatabaseSelection(Guid companyId, string connectionString)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Wymagana firma.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        CompanyId = companyId;
        ConnectionString = connectionString;
    }

    public Guid CompanyId { get; }
    internal string ConnectionString { get; }

    public override string ToString() => $"Wybrana baza firmy {CompanyId}";
}
