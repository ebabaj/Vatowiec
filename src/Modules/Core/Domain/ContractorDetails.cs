namespace Vatowiec.Core.Domain;

public sealed class AddressType
{
    private AddressType() { }
    public AddressType(string name) => Name = EntityText.Required(name, 100, nameof(name));
    public int Id { get; private set; }
    public string Name { get; private set; } = null!;
}

public sealed class ContractorAddress
{
    private ContractorAddress() { }
    public ContractorAddress(int contractorId, int addressTypeId)
    {
        if (contractorId <= 0 || addressTypeId <= 0) throw new ArgumentException("Brak kontrahenta lub rodzaju adresu.");
        ContractorId = contractorId;
        AddressTypeId = addressTypeId;
    }
    public int Id { get; private set; }
    public int ContractorId { get; private set; }
    public int AddressTypeId { get; private set; }
    public string? Name { get; set; }
    public string? Street { get; set; }
    public string? BuildingNo { get; set; }
    public string? ApartmentNo { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? CountryCode { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; private set; } = [];

}

public sealed class ContractorBankAccount
{
    private ContractorBankAccount() { }
    public ContractorBankAccount(int contractorId, string accountNumber, string? currencyCode = null)
    {
        if (contractorId <= 0) throw new ArgumentException("Brak kontrahenta.", nameof(contractorId));
        ContractorId = contractorId;
        AccountNumber = EntityText.Required(accountNumber, 100, nameof(accountNumber));
        CurrencyCode = EntityText.Optional(currencyCode, 3, nameof(currencyCode))?.ToUpperInvariant();
    }
    public int Id { get; private set; }
    public int ContractorId { get; private set; }
    public string AccountNumber { get; private set; } = null!;
    public string? BankName { get; set; }
    public string? Bic { get; set; }
    public string? CurrencyCode { get; private set; }
    public string? AccountHolderName { get; set; }
    public string? Name { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public byte[] RowVersion { get; private set; } = [];

    public void SetNumberAndCurrency(string accountNumber, string? currencyCode)
    {
        AccountNumber = EntityText.Required(accountNumber, 100, nameof(accountNumber));
        CurrencyCode = EntityText.Optional(currencyCode, 3, nameof(currencyCode))?.ToUpperInvariant();
    }
}

public sealed class ContractorContact
{
    private ContractorContact() { }
    public ContractorContact(int contractorId, string name)
    {
        if (contractorId <= 0) throw new ArgumentException("Brak kontrahenta.", nameof(contractorId));
        ContractorId = contractorId;
        Name = EntityText.Required(name, 256, nameof(name));
    }
    public void Rename(string name) => Name = EntityText.Required(name, 256, nameof(name));
    public int Id { get; private set; }
    public int ContractorId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Position { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Street { get; set; }
    public string? BuildingNo { get; set; }
    public string? ApartmentNo { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }
    public string? Notes { get; set; }
    public byte[] RowVersion { get; private set; } = [];
}
