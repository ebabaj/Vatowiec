using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

public static class TableViewEndpoints
{
    public static void MapTableViews(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/companies/{companyId:guid}/table-views").RequireAuthorization()
            .AddEndpointFilter(CompanyEndpoints.ValidateCompanySource);
        group.MapGet("/{tableId}", Read);
        group.MapPut("/{tableId}/mine", (Guid companyId, string tableId, TableViewRequest value, HttpContext http,
            SystemDbContext db, IAntiforgery csrf, TimeProvider clock) => Write(companyId, tableId, value, false, http, db, csrf, clock));
        group.MapPut("/{tableId}/installation", (Guid companyId, string tableId, TableViewRequest value, HttpContext http,
            SystemDbContext db, IAntiforgery csrf, TimeProvider clock) => Write(companyId, tableId, value, true, http, db, csrf, clock));
    }
    private static Guid User(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);
    private static async Task<string[]?> Permissions(Guid companyId, string tableId, HttpContext http, SystemDbContext db)
    {
        if (TableCatalog.Find(tableId) is not { } table) return null;
        var permission = await (from access in db.CompanyAccesses.AsNoTracking()
                                join company in db.Companies.AsNoTracking() on access.CompanyId equals company.Id
                                where access.UserId == User(http) && access.CompanyId == companyId && company.IsActive
                                select (CompanyPermission?)access.Permissions).SingleOrDefaultAsync(http.RequestAborted);
        var codes = UserAdministrationService.Codes(permission ?? CompanyPermission.None);
        return codes.Contains(table.ReadPermission, StringComparer.Ordinal) ? codes : null;
    }
    private static async Task<IResult> Read(Guid companyId, string tableId, HttpContext http, SystemDbContext db)
    {
        var permissions = await Permissions(companyId, tableId, http, db);
        if (permissions == null) return Error(403, "table_access_denied", "Brak dostępu do tabeli w tej firmie.");
        return Results.Ok(await Response(tableId, User(http), permissions, db, http.RequestAborted));
    }
    private static string[]? Columns(TableView? row) => row?.ColumnsJson == null ? null : JsonSerializer.Deserialize<string[]>(row.ColumnsJson);
    private static string? Version(TableView? row) => row == null ? null : Convert.ToBase64String(row.RowVersion);
    private static async Task<TableViewResponse> Response(string tableId, Guid userId, string[] permissions, SystemDbContext db, CancellationToken ct)
    {
        var installation = await db.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleAsync(ct);
        var rows = await db.TableViews.AsNoTracking().Where(x => x.InstallationId == installation && x.TableId == tableId &&
            (x.OwnerId == userId || x.OwnerId == Guid.Empty)).ToArrayAsync(ct);
        var own = rows.SingleOrDefault(x => x.OwnerId == userId); var global = rows.SingleOrDefault(x => x.OwnerId == Guid.Empty);
        var table = TableCatalog.Find(tableId)!;
        return new(tableId, table.Available(permissions), TableCatalog.EffectiveColumns(table, permissions, Columns(own), Columns(global)),
            own?.PageSize ?? 50, own?.ColumnsJson != null ? "User" : global?.ColumnsJson != null ? "Installation" : "Factory", Version(own), Version(global));
    }
    private static async Task<IResult> Write(Guid companyId, string tableId, TableViewRequest value, bool global,
        HttpContext http, SystemDbContext db, IAntiforgery csrf, TimeProvider clock)
    {
        try { await csrf.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania."); }
        if (value.PageSize.HasValue && (!TableCatalog.PageSizes.Contains(value.PageSize.Value) || global) ||
            value.Reset && (value.Columns != null || value.PageSize != null) ||
            !value.Reset && value.Columns == null && value.PageSize == null)
            return Error(400, "table_view_invalid", "Wybierz kolumny lub prawidłowy rozmiar strony.");
        await using var tx = await db.Database.BeginTransactionAsync(http.RequestAborted);
        // Same lock as role/access mutations: check the current role and company permissions inside the transaction.
        await UserAdministrationService.Lock(db, http.RequestAborted);
        var userId = User(http);
        var permissions = await Permissions(companyId, tableId, http, db);
        if (permissions == null || global && !await InstallationAdministration.IsAdministrator(db, userId, http.RequestAborted))
            return Error(403, "table_access_denied", "Brak uprawnienia do zmiany tego widoku.");
        var sessionId = Guid.Parse(http.User.FindFirstValue(ApiSecurity.SessionClaim)!);
        if (!await db.UserSessions.AsNoTracking().AnyAsync(x => x.Id == sessionId && x.UserId == userId &&
            x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.GetUtcNow(), http.RequestAborted))
            return Error(401, "session_revoked", "Sesja została unieważniona.");
        var available = TableCatalog.Find(tableId)!.Available(permissions).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        if (value.Columns is { } selected && (selected.Length == 0 || selected.Length > available.Count ||
            selected.Distinct(StringComparer.Ordinal).Count() != selected.Length || selected.Any(c => !available.Contains(c))))
            return Error(400, "table_columns_invalid", "Wybierz co najmniej jedną dostępną kolumnę. Odśwież konfigurację.");
        var installation = await db.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleAsync(http.RequestAborted);
        var owner = global ? Guid.Empty : userId;
        var row = await db.TableViews.SingleOrDefaultAsync(x => x.InstallationId == installation && x.OwnerId == owner && x.TableId == tableId, http.RequestAborted);
        if (Version(row) != value.RowVersion)
            return Error(409, "table_view_stale", "Widok zmienił się na innym stanowisku. Odśwież konfigurację i ponów wybór.");
        var before = row?.ColumnsJson;
        if (value.Reset) { if (row != null) db.TableViews.Remove(row); }
        else
        {
            if (row == null) { row = new() { InstallationId = installation, OwnerId = owner, TableId = tableId }; db.TableViews.Add(row); }
            if (value.Columns != null) row.ColumnsJson = JsonSerializer.Serialize(value.Columns);
            if (value.PageSize != null) row.PageSize = value.PageSize;
        }
        if (global && (before != (value.Reset ? null : row?.ColumnsJson)))
            db.TableViewChanges.Add(new()
            {
                InstallationId = installation,
                TableId = tableId,
                ActorId = userId,
                ActorName = http.User.Identity!.Name!,
                AtUtc = clock.GetUtcNow(),
                BeforeJson = before,
                AfterJson = value.Reset ? null : row?.ColumnsJson
            });
        try { await db.SaveChangesAsync(http.RequestAborted); await tx.CommitAsync(http.RequestAborted); }
        catch (DbUpdateConcurrencyException) { return Error(409, "table_view_stale", "Widok zmienił się. Odśwież konfigurację."); }
        catch (DbUpdateException) { return Error(503, "table_view_write_failed", "Nie zapisano widoku i audytu. Odśwież konfigurację."); }
        return Results.Ok(await Response(tableId, userId, permissions, db, http.RequestAborted));
    }
}
