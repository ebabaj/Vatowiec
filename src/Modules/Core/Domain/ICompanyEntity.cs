namespace Vatowiec.Core.Domain;

public interface ICompanyEntity : ITrackedEntity
{
    Guid CompanyId { get; }
}
