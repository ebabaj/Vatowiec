namespace Vatowiec.Core.Domain;

public interface ITrackedEntity
{
    DateTimeOffset CreatedAtUtc { get; }
    DateTimeOffset UpdatedAtUtc { get; }
    byte[] RowVersion { get; }
}
