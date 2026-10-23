using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vatowiec.Core.Infrastructure.Registries;

public sealed record NbpRate(string Code, decimal? Mid, decimal? Bid, decimal? Ask);
public sealed record NbpTable(string Type, string Number, DateOnly EffectiveDate, DateOnly? TradingDate, NbpRate[] Rates);
public sealed record NbpReply(NbpTable[] Tables, string? Error);
public interface INbpClient
{
    Task<NbpReply> Fetch(string table, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}
public sealed class NbpClient(IHttpClientFactory clients) : INbpClient
{
    public async Task<NbpReply> Fetch(string table, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (table is not ("A" or "B" or "C") || from.HasValue != to.HasValue ||
            from != null && (to < from || to!.Value.DayNumber - from.Value.DayNumber > 92))
            throw new ArgumentException("Nieprawidłowe parametry NBP.");
        try
        {
            using var http = clients.CreateClient("nbp");
            var path = "https://api.nbp.pl/api/exchangerates/tables/" + table + "/" +
                (from == null ? "" : from.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/" + to!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/") + "?format=json";
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return new([], null);
            if (!response.IsSuccessStatusCode) return new([], "NBP " + table + ": HTTP " + (int)response.StatusCode + ". Nie pobrano wiarygodnej tabeli.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var result = new List<NbpTable>();
            foreach (var element in json.RootElement.EnumerateArray())
            {
                var type = element.GetProperty("table").GetString();
                var number = element.GetProperty("no").GetString() ?? "";
                var effective = DateOnly.ParseExact(element.GetProperty("effectiveDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var trading = type == "C" && element.TryGetProperty("tradingDate", out var tradingElement) && tradingElement.ValueKind != JsonValueKind.Null
                    ? DateOnly.ParseExact(tradingElement.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture) : (DateOnly?)null;
                if (type != table || number.Length > 32 || !Regex.IsMatch(number, "^[0-9]{1,3}/" + table + "/NBP/[0-9]{4}$") ||
                    !number.EndsWith("/" + effective.Year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
                    from != null && (effective < from || effective > to) || trading > effective || effective < new DateOnly(2002, 1, 2)) throw new InvalidDataException();
                var rates = new List<NbpRate>();
                foreach (var value in element.GetProperty("rates").EnumerateArray())
                {
                    var code = value.GetProperty("code").GetString() ?? "";
                    if (!Regex.IsMatch(code, "^[A-Z]{3}$") || code == "PLN" || rates.Any(x => x.Code == code)) throw new InvalidDataException();
                    decimal Amount(string field)
                    {
                        var elementValue = value.GetProperty(field);
                        var raw = elementValue.GetRawText();
                        var parts = Regex.Match(raw, @"^-?(?<whole>\d+)(?:\.(?<fraction>\d+))?(?:[Ee](?<exponent>[+-]?\d+))?$");
                        if (!parts.Success) throw new InvalidDataException();
                        var fraction = parts.Groups["fraction"].Value;
                        var digits = parts.Groups["whole"].Value + fraction;
                        var exponent = parts.Groups["exponent"].Success ? int.Parse(parts.Groups["exponent"].Value, CultureInfo.InvariantCulture) : 0;
                        if ((long)fraction.Length - exponent - (digits.Length - digits.TrimEnd('0').Length) > 12) throw new InvalidDataException();
                        var amount = elementValue.GetDecimal();
                        if (amount <= 0 || amount >= 1_000_000_000_000m || decimal.Round(amount, 12) != amount) throw new InvalidDataException();
                        return amount;
                    }
                    var rate = type == "C" ? new NbpRate(code, null, Amount("bid"), Amount("ask")) : new(code, Amount("mid"), null, null);
                    if (rate.Bid > rate.Ask) throw new InvalidDataException();
                    rates.Add(rate);
                }
                if (rates.Count == 0 || result.Any(x => x.EffectiveDate == effective)) throw new InvalidDataException();
                result.Add(new(table, number, effective, trading, rates.ToArray()));
            }
            if (from == null && result.Count != 1) throw new InvalidDataException();
            return new(result.ToArray(), null);
        }
        catch (OperationCanceledException) { return new([], "NBP " + table + ": przekroczono czas oczekiwania; nie jest to brak publikacji."); }
        catch (HttpRequestException) { return new([], "NBP " + table + ": błąd połączenia; nie jest to brak publikacji."); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException or InvalidDataException)
        { return new([], "NBP " + table + ": niepoprawna odpowiedź; nie zapisano tej partii."); }
    }
}
