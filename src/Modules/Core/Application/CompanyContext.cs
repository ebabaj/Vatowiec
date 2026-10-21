namespace Vatowiec.Core.Application;

// W przyszłości tworzony wyłącznie po serwerowej autoryzacji użytkownika.
public interface ICompanyContext
{
    Guid? CompanyId { get; }
}

public sealed record CompanyContext(Guid? CompanyId) : ICompanyContext;
