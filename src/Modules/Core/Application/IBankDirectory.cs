namespace Vatowiec.Core.Application;

public sealed record BankDirectoryResult(string Status, string Message, string ClearingNumber,
    string? BankName = null, string[]? Bics = null);

public interface IBankDirectory
{
    Task<BankDirectoryResult> Find(string clearingNumber, CancellationToken cancellationToken);
}
