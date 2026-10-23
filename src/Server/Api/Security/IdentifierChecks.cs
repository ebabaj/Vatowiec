using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

public sealed class IdentifierChecks(SystemDbContext system, TaxIdentifierValidator validator, TimeProvider clock)
{
    public async Task<IdentifierPreview> Check(CompanyDbContext company, Guid companyId, Guid actor,
        ContractorWriteRequest value, CancellationToken ct)
    {
        var rules = await system.TaxIdentifierSchemes.AsNoTracking().ToListAsync(ct);
        var date = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var taxChecks = new[] { ("TaxId", value.TaxId), ("VatEuId", value.VatEuId), ("KsefIdWew", value.KsefIdWew) }
            .Select(x => validator.Assess(x.Item1, x.Item2, value.CountryCode.Trim().ToUpperInvariant(), rules, date))
            .Concat(new[] { ("Regon", value.Regon), ("Krs", value.Krs), ("Bdo", value.Bdo), ("Pesel", value.Pesel) }
                .Select(x => StandardIdentifiers.Assess(x.Item1, x.Item2)));
        var checks = taxChecks.Select(x => new IdentifierCheck(x.Field, x.Normalized, x.Status, x.Message, x.RuleVersion, x.FormatChecked, x.ChecksumChecked)).ToArray();
        var tax = checks[0].Normalized; var vat = checks[1].Normalized; var ksef = checks[2].Normalized;
        var duplicates = await company.Contractors.AsNoTracking().Where(x => x.Id != value.Id &&
            ((tax != null && x.TaxId == tax) || (vat != null && x.VatEuId == vat) || (ksef != null && x.KsefIdWew == ksef)))
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Name, x.RowVersion, x.TaxId, x.VatEuId, x.KsefIdWew }).ToListAsync(ct);
        var matches = duplicates.SelectMany(x => new[] { ("TaxId", tax is not null && x.TaxId == tax), ("VatEuId", vat is not null && x.VatEuId == vat), ("KsefIdWew", ksef is not null && x.KsefIdWew == ksef) }
            .Where(y => y.Item2).Select(y => new IdentifierDuplicate(y.Item1, x.Id, x.Code, x.Name, Convert.ToBase64String(x.RowVersion)))).ToArray();
        var hash = Hash(JsonSerializer.Serialize(new
        {
            companyId,
            actor,
            value.Id,
            value.Code,
            value.Name,
            value.CountryCode,
            value.IsActive,
            value.ParentId,
            value.RowVersion,
            value.OperationId,
            Checks = checks,
            Duplicates = matches
        }));
        return new(checks, matches, hash, checks.Any(x => x.Status is "Invalid" or "Unknown") || matches.Length != 0);
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
