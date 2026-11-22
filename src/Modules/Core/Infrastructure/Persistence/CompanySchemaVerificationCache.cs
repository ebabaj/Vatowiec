using System.Collections.Concurrent;

namespace Vatowiec.Core.Infrastructure.Persistence;

/// <summary>
/// Krótkotrwała pamięć udanej weryfikacji historii migracji bazy firmy (ADR-046).
/// Przechowuje wyłącznie fakt zgodności schematu dla serwera, bazy i firmy; nie zastępuje
/// sprawdzenia CompanyAccess, aktywności firmy ani profilu firmy, które resolver wykonuje
/// przy każdym żądaniu. Niezgodność nigdy nie jest zapamiętywana.
/// </summary>
public sealed class CompanySchemaVerificationCache(TimeProvider? time = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Key, DateTimeOffset> verified = new();

    public bool IsVerified(string server, string database, Guid companyId) =>
        verified.TryGetValue(new(server.ToUpperInvariant(), database.ToUpperInvariant(), companyId), out var until) &&
        until > clock.GetUtcNow();

    public void MarkVerified(string server, string database, Guid companyId) =>
        verified[new(server.ToUpperInvariant(), database.ToUpperInvariant(), companyId)] = clock.GetUtcNow() + Lifetime;

    public void Forget(string server, string database, Guid companyId) =>
        verified.TryRemove(new(server.ToUpperInvariant(), database.ToUpperInvariant(), companyId), out _);

    private readonly record struct Key(string Server, string Database, Guid CompanyId);
}
