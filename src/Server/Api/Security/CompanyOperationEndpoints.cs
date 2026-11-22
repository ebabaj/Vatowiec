using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

// ADR-044 krok 1: API przyjmuje zlecenie i pokazuje stan. Nie tworzy baz, nie wykonuje migracji ani DDL.
public static class CompanyOperationEndpoints
{
    private const int PageSize = 50;

    public static void MapCompanyOperations(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/installation/company-operations")
            .RequireAuthorization(InstallationAdministration.Policy);
        group.MapGet("", async (SystemDbContext system, int offset = 0, CancellationToken ct = default) =>
        {
            if (offset is < 0 or > 1_000_000) return Error(400, "invalid_page", "Nieprawidłowa strona wyników.");
            var rows = await system.CompanyOperations.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
                .Skip(offset).Take(PageSize + 1).ToArrayAsync(ct);
            return Results.Ok(rows.Select(Response));
        });
        group.MapGet("/server-profiles", (IConfiguration configuration) => Results.Ok(Profiles(configuration)));
        group.MapPost("/provision", Provision);
        group.MapPost("/{operationId:guid}/retry", Retry);
        group.MapPost("/delete", async (DeleteCompanyRequest value, HttpContext http, IAntiforgery csrf, CompanyOperationRequests requests) =>
        {
            if (!await ValidCsrf(csrf, http)) return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania.");
            try
            {
                var (operation, created) = await requests.RequestDelete(Requester(http), value.OperationKey, value.CompanyId,
                    value.CreateArchive, value.ConfirmWithoutArchive, http.RequestAborted);
                return created ? Results.Json(Response(operation), statusCode: 202) : Results.Ok(Response(operation));
            }
            catch (CompanyOperationException error) { return Error(error.Status, error.Code, error.Message); }
        });
        group.MapPost("/{operationId:guid}/cancel", async (Guid operationId, CompanyOperationRetryRequest value, HttpContext http,
            IAntiforgery csrf, CompanyOperationRequests requests) =>
        {
            if (!await ValidCsrf(csrf, http)) return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania.");
            try { return Results.Ok(Response(await requests.Cancel(Requester(http), operationId, value.RowVersion, http.RequestAborted))); }
            catch (CompanyOperationException error) { return Error(error.Status, error.Code, error.Message); }
        });

        // Katalog firm instalacji (tylko nazwy i bazy, bez danych firm) i rejestr usuniętych firm.
        var installation = app.MapGroup("/api/v1/installation").RequireAuthorization(InstallationAdministration.Policy);
        installation.MapGet("/companies", async (SystemDbContext system, CancellationToken ct) => Results.Ok(await (
            from company in system.Companies.AsNoTracking()
            join database in system.CompanyDatabases.AsNoTracking() on company.Id equals database.CompanyId
            orderby company.Name
            select new InstallationCompanyResponse(company.Id, company.Name, database.DatabaseName, company.IsActive)).ToArrayAsync(ct)));
        installation.MapGet("/deleted-companies", async (SystemDbContext system, CancellationToken ct) => Results.Ok(
            (await system.DeletedCompanies.AsNoTracking().OrderByDescending(x => x.DeletedAtUtc).Take(200).ToArrayAsync(ct))
            .Select(x => new DeletedCompanyResponse(x.CompanyId, x.CompanyName, x.Nip, x.Regon, x.DeletedAtUtc, x.DeletedByName, x.ArchiveFileName != null))));
    }

    private static async Task<IResult> Provision(ProvisionCompanyRequest value, HttpContext http, IAntiforgery csrf,
        CompanyOperationRequests requests, IConfiguration configuration)
    {
        if (!await ValidCsrf(csrf, http)) return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania.");
        var profile = Profiles(configuration).SingleOrDefault(x => x.Key == value.ConnectionProfileKey?.Trim());
        if (profile is null) return Error(400, "server_profile_invalid", "Wybierz serwer baz danych z listy skonfigurowanych na serwerze API.");
        try
        {
            var (operation, created) = await requests.RequestProvision(Requester(http), value.OperationKey, value.CompanyName,
                profile.Key, profile.ServerName, http.RequestAborted, (name, ct) => DatabaseExists(configuration, profile.Key, name, ct));
            return created ? Results.Json(Response(operation), statusCode: 202) : Results.Ok(Response(operation));
        }
        catch (CompanyOperationException error) { return Error(error.Status, error.Code, error.Message); }
        catch (SqlException)
        {
            return Error(503, "server_unavailable", "Nie można sprawdzić serwera baz firm. Nie przyjęto zlecenia; spróbuj ponownie.");
        }
    }

    // Wyłącznie odczyt metadanych sys.databases kontem API; żadnego DDL.
    private static async Task<bool> DatabaseExists(IConfiguration configuration, string profileKey, string database, CancellationToken ct)
    {
        var connection = new SqlConnectionStringBuilder(configuration[$"CompanyConnections:{profileKey}"]) { InitialCatalog = "master" };
        await using var sql = new SqlConnection(connection.ConnectionString);
        await sql.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @name", sql);
        command.Parameters.AddWithValue("@name", database);
        return (int)(await command.ExecuteScalarAsync(ct))! > 0;
    }

    private static async Task<IResult> Retry(Guid operationId, CompanyOperationRetryRequest value, HttpContext http,
        IAntiforgery csrf, CompanyOperationRequests requests)
    {
        if (!await ValidCsrf(csrf, http)) return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania.");
        try { return Results.Ok(Response(await requests.Retry(Requester(http), operationId, value.RowVersion, http.RequestAborted))); }
        catch (CompanyOperationException error) { return Error(error.Status, error.Code, error.Message); }
    }

    // Wyłącznie klucz i nazwa serwera z konfiguracji API; connection string nigdy nie opuszcza serwera.
    internal static ServerProfileResponse[] Profiles(IConfiguration configuration) => configuration.GetSection("CompanyConnections")
        .GetChildren().Select(x =>
        {
            try { return new ServerProfileResponse(x.Key, new SqlConnectionStringBuilder(x.Value).DataSource); }
            catch (ArgumentException) { return null; }
        })
        .Where(x => x is not null && x.Key.Length <= 128 && !string.IsNullOrWhiteSpace(x.ServerName))
        .Select(x => x!).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();

    private static async Task<bool> ValidCsrf(IAntiforgery csrf, HttpContext http)
    {
        try { await csrf.ValidateRequestAsync(http); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private static OperationRequester Requester(HttpContext http) =>
        new(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!), http.User.Identity?.Name ?? "");

    internal static CompanyOperationResponse Response(CompanyOperation x) => new(x.Id, x.Kind, x.CompanyId, x.CompanyName, x.Stage,
        x.Status, x.Attempt, x.LastErrorCode, x.RequestedByName, x.CreatedAtUtc, x.UpdatedAtUtc, x.CompletedAtUtc,
        Convert.ToBase64String(x.RowVersion), x.CreateArchive);

    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);
}
