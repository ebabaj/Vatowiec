using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.Persistence;

public sealed class ContractorTableRow
{
    public Contractor Contractor { get; init; } = null!;
    public ContractorAddress? Address { get; init; }
    public string? ParentName { get; init; }
}

public static class ContractorTableQuery
{
    // AddressTypes.Id=1 is the immutable seeded registered-address type, not the first address/type.
    // Within that type: preferred, active, then lowest identity. No correspondence addresses or inheritance.
    public static IQueryable<ContractorTableRow> Rows(CompanyDbContext db) =>
        from c in db.Contractors.AsNoTracking()
        from a in db.ContractorAddresses.AsNoTracking().Where(a => a.ContractorId == c.Id && a.AddressTypeId == 1)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.IsActive).ThenBy(a => a.Id).Take(1).DefaultIfEmpty()
        select new ContractorTableRow
        {
            Contractor = c,
            Address = a,
            ParentName = db.Contractors.Where(p => p.Id == c.ParentId).Select(p => p.Code + " — " + p.Name).FirstOrDefault()
        };

    public static IQueryable<ContractorTableRow> Search(IQueryable<ContractorTableRow> rows, string[] columns, string? query)
    {
        var search = query?.Trim();
        if (string.IsNullOrEmpty(search)) return rows;
        // Capture the value so EF extracts a SQL parameter. Expression.Constant(search)
        // would inline identifiers (including PESEL) into logged command text.
        Expression<Func<string>> searchValue = () => search;
        var parameter = Expression.Parameter(typeof(ContractorTableRow), "row");
        Expression body = Expression.Constant(false);
        foreach (var id in columns)
        {
            Expression<Func<ContractorTableRow, string?>> field = id switch
            {
                "Code" => r => r.Contractor.Code,
                "Name" => r => r.Contractor.Name,
                "TaxId" => r => r.Contractor.TaxId,
                "CountryCode" => r => r.Contractor.CountryCode,
                "VatEuId" => r => r.Contractor.VatEuId,
                "Regon" => r => r.Contractor.Regon,
                "Krs" => r => r.Contractor.Krs,
                "Bdo" => r => r.Contractor.Bdo,
                "Pesel" => r => r.Contractor.Pesel,
                "KsefIdWew" => r => r.Contractor.KsefIdWew,
                "ParentName" => r => r.ParentName,
                "IsActive" => r => r.Contractor.IsActive ? "Aktywny" : "Nieaktywny",
                "AddressName" => r => r.Address == null ? null : r.Address.Name,
                "AddressCity" => r => r.Address == null ? null : r.Address.City,
                "AddressStreet" => r => r.Address == null ? null : r.Address.Street,
                "AddressBuildingNo" => r => r.Address == null ? null : r.Address.BuildingNo,
                "AddressApartmentNo" => r => r.Address == null ? null : r.Address.ApartmentNo,
                "AddressPostalCode" => r => r.Address == null ? null : r.Address.PostalCode,
                "AddressRegion" => r => r.Address == null ? null : r.Address.Region,
                "AddressCountryCode" => r => r.Address == null ? null : r.Address.CountryCode,
                "AddressIsActive" => r => r.Address == null ? null : r.Address.IsActive ? "Aktywny" : "Nieaktywny",
                "AddressIsDefault" => r => r.Address == null ? null : r.Address.IsDefault ? "Tak" : "Nie",
                "CreatedAtUtc" => r => r.Contractor.CreatedAtUtc.Year.ToString() + "-" +
                    (r.Contractor.CreatedAtUtc.Month < 10 ? "0" : "") + r.Contractor.CreatedAtUtc.Month.ToString() + "-" +
                    (r.Contractor.CreatedAtUtc.Day < 10 ? "0" : "") + r.Contractor.CreatedAtUtc.Day.ToString() + " " +
                    (r.Contractor.CreatedAtUtc.Hour < 10 ? "0" : "") + r.Contractor.CreatedAtUtc.Hour.ToString() + ":" +
                    (r.Contractor.CreatedAtUtc.Minute < 10 ? "0" : "") + r.Contractor.CreatedAtUtc.Minute.ToString() + " UTC",
                "UpdatedAtUtc" => r => r.Contractor.UpdatedAtUtc.Year.ToString() + "-" +
                    (r.Contractor.UpdatedAtUtc.Month < 10 ? "0" : "") + r.Contractor.UpdatedAtUtc.Month.ToString() + "-" +
                    (r.Contractor.UpdatedAtUtc.Day < 10 ? "0" : "") + r.Contractor.UpdatedAtUtc.Day.ToString() + " " +
                    (r.Contractor.UpdatedAtUtc.Hour < 10 ? "0" : "") + r.Contractor.UpdatedAtUtc.Hour.ToString() + ":" +
                    (r.Contractor.UpdatedAtUtc.Minute < 10 ? "0" : "") + r.Contractor.UpdatedAtUtc.Minute.ToString() + " UTC",
                _ => throw new ArgumentException("Nieznana kolumna wyszukiwania.")
            };
            var expression = new ReplaceParameter(field.Parameters[0], parameter).Visit(field.Body)!;
            var contains = Expression.Call(expression, nameof(string.Contains), Type.EmptyTypes, searchValue.Body);
            body = Expression.OrElse(body, Expression.AndAlso(Expression.NotEqual(expression, Expression.Constant(null, typeof(string))), contains));
        }
        return rows.Where(Expression.Lambda<Func<ContractorTableRow, bool>>(body, parameter));
    }
    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    { protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node); }
}
