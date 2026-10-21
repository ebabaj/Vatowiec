namespace Vatowiec.Core.Domain;

public interface ICompanyEntity
{
    Guid CompanyId { get; }
    DateTimeOffset CreatedAtUtc { get; }
    DateTimeOffset UpdatedAtUtc { get; }
    byte[] RowVersion { get; }
}
