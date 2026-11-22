using System.Security.Claims;

namespace Vatowiec.Api.Endpoints;

/// <summary>Wspólne odpowiedzi i dane żądania dla endpointów API (ADR-046).</summary>
internal static class EndpointResults
{
    /// <summary>Błąd biznesowy w stałym kształcie <c>{ code, message }</c>, bez szczegółów wyjątku.</summary>
    public static IResult Error(int status, string code, string message) =>
        Results.Json(new { code, message }, statusCode: status);

    /// <summary>Identyfikator zalogowanego użytkownika; endpoint wymaga uwierzytelnienia.</summary>
    public static Guid UserId(this HttpContext http) =>
        Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
