using System.Text.Json;
using Vatowiec.Core.Domain;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

internal static class ContractorDetailMapping
{
    public static string Type(string kind) => kind switch
    {
        "addresses" => "ContractorAddress",
        "contacts" => "ContractorContact",
        _ => "ContractorBankAccount"
    };
    public static DetailValues Values(object entity) => JsonSerializer.SerializeToElement(entity).Deserialize<DetailValues>()!;
    public static int Id(object entity) => (int)entity.GetType().GetProperty("Id")!.GetValue(entity)!;
    public static byte[] Version(object entity) => (byte[])entity.GetType().GetProperty("RowVersion")!.GetValue(entity)!;
    public static DetailResponse Response(object entity, string kind, Guid companyId, int contractorId) =>
        new(Id(entity), companyId, contractorId, kind, Values(entity), Convert.ToBase64String(Version(entity)));
    public static string Snapshot(object entity) => JsonSerializer.Serialize(Values(entity));
    public static object New(string kind, int contractorId, DetailValues data) => kind switch
    {
        "addresses" => new ContractorAddress(contractorId, data.AddressTypeId),
        "contacts" => new ContractorContact(contractorId, data.Name!),
        _ => new ContractorBankAccount(contractorId, data.AccountNumber!, data.CurrencyCode)
    };
    public static void Apply(object entity, DetailValues data)
    {
        if (entity is ContractorContact contact) contact.Rename(data.Name!);
        if (entity is ContractorBankAccount bank) bank.SetNumberAndCurrency(data.AccountNumber!, data.CurrencyCode);
        // Tylko jawne pola DTO i publiczne settery istniejącej encji. Id/FK/rowversion nie są przypisywane.
        foreach (var property in typeof(DetailValues).GetProperties())
        {
            var target = entity.GetType().GetProperty(property.Name);
            if (target?.SetMethod?.IsPublic != true) continue;
            var value = property.GetValue(data);
            if (value is string text)
            {
                value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                if (property.Name is "CountryCode" or "Bic") value = ((string?)value)?.ToUpperInvariant();
            }
            target.SetValue(entity, value);
        }
    }
}
