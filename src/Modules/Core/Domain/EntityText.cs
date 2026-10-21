namespace Vatowiec.Core.Domain;

internal static class EntityText
{
    public static string Required(string value, int maxLength, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        var result = value.Trim();
        if (result.Length > maxLength)
        {
            throw new ArgumentException($"Maksymalna długość: {maxLength}.", parameter);
        }

        return result;
    }

    public static string? Optional(string? value, int maxLength, string parameter) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maxLength, parameter);

    public static Guid Id(Guid value, string parameter) => value != Guid.Empty
        ? value
        : throw new ArgumentException("Identyfikator nie może być pusty.", parameter);
}
