using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence;

// Wewnętrzny zapis modelu; nie udostępnia nowych endpointów ani ekranów rachunków.
public static class ContractorBankAccountDefaults
{
    public static async Task SetDefault(CompanyDbContext db, int accountId, byte[] version,
        Guid actorId, Guid issuerId, string actorName, DateTimeOffset now, string correlationId,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CompanyWriteLock.Acquire(db, "ContractorBankAccountDefaults", ct);
        var selected = await db.ContractorBankAccounts.SingleAsync(x => x.Id == accountId, ct);
        if (!selected.RowVersion.SequenceEqual(version)) throw new DbUpdateConcurrencyException("Rachunek zmienił się od odczytu.");
        var accounts = await db.ContractorBankAccounts.Where(x => x.ContractorId == selected.ContractorId &&
            x.CurrencyCode == selected.CurrencyCode).OrderBy(x => x.Id).ToListAsync(ct);
        if (selected.IsDefault) return;
        var before = JsonSerializer.Serialize(new { BankAccounts = accounts });
        foreach (var account in accounts) account.IsDefault = false;
        // Oddzielne UPDATE w tej samej transakcji zwalnia unikat przed zmianą nowego.
        await db.SaveChangesAsync(ct);
        selected.IsDefault = true;
        await db.SaveChangesAsync(ct);
        db.ContractorChanges.Add(new ContractorChange(selected.ContractorId, actorId, issuerId, actorName,
            now, correlationId, before, JsonSerializer.Serialize(new { BankAccounts = accounts })));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
