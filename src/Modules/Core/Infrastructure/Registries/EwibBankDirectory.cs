using System.Net.Http.Headers;
using System.Text.Json;
using Vatowiec.Core.Application;

namespace Vatowiec.Core.Infrastructure.Registries;

public sealed class EwibBankDirectory(IHttpClientFactory clients) : IBankDirectory
{
    public async Task<BankDirectoryResult> Find(string clearingNumber, CancellationToken cancellationToken)
    {
        // Brak parametru oznacza w EWIB cały wykaz; nigdy nie wysyłamy pustego filtra.
        if (clearingNumber.Length != 8 || !clearingNumber.All(char.IsAsciiDigit))
            throw new ArgumentException("Numer rozliczeniowy musi mieć osiem cyfr.", nameof(clearingNumber));
        BankDirectoryResult Error(string status, string message) => new(status, message + " Możesz uzupełnić bank ręcznie.", clearingNumber);
        try
        {
            using var http = clients.CreateClient("ewib");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://ewib.nbp.pl/api/v1/zapytanie1/?nrRozliczeniowy=" + clearingNumber + "&format=json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return Error("Unavailable", $"NBP EWIB: nie pobrano danych (HTTP {(int)response.StatusCode}).");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var owners = json.RootElement.GetProperty("listaWlascicieli");
            if (owners.ValueKind != JsonValueKind.Array) throw new InvalidDataException();
            if (owners.GetArrayLength() == 0) return Error("NotFound", "NBP EWIB: nie znaleziono numeru rozliczeniowego.");
            var matches = new List<(string Owner, string Name, string[] Bics)>();
            foreach (var owner in owners.EnumerateArray())
            {
                var units = new List<JsonElement>();
                if (owner.TryGetProperty("siedziba", out var office) && office.ValueKind != JsonValueKind.Null) units.Add(office);
                if (owner.TryGetProperty("jednostki", out var branches) && branches.ValueKind != JsonValueKind.Null)
                    units.AddRange(branches.EnumerateArray());
                foreach (var unit in units)
                {
                    if (!unit.TryGetProperty("numeryRozliczeniowe", out var codes) || codes.ValueKind == JsonValueKind.Null) continue;
                    foreach (var code in codes.EnumerateArray())
                    {
                        if (code.GetProperty("numer").GetString() != clearingNumber) continue;
                        var id = owner.GetProperty("numer").GetString()?.Trim();
                        var name = owner.GetProperty("nazwa").GetString()?.Trim();
                        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || name.Length > 200) throw new InvalidDataException();
                        var bics = new List<string>();
                        if (code.TryGetProperty("numeryBic", out var numbers) && numbers.ValueKind != JsonValueKind.Null)
                            foreach (var entry in numbers.EnumerateArray())
                            {
                                var bic = entry.GetProperty("numer").GetString()?.Trim().ToUpperInvariant();
                                if (string.IsNullOrEmpty(bic)) continue;
                                if (bic.Length is not (8 or 11) || !bic.All(char.IsAsciiLetterOrDigit)) throw new InvalidDataException();
                                bics.Add(bic);
                            }
                        matches.Add((id, name, bics.ToArray()));
                    }
                }
            }
            if (matches.Count == 0) throw new InvalidDataException();
            if (matches.Select(x => x.Owner).Distinct(StringComparer.Ordinal).Count() != 1 ||
                matches.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != 1)
                return Error("Ambiguous", "NBP EWIB: niejednoznaczna instytucja; pola pozostawiono bez zmian.");
            return new("Found", "Dane banku pobrano z NBP EWIB", clearingNumber, matches[0].Name,
                matches.SelectMany(x => x.Bics).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Error("Unavailable", "NBP EWIB: przekroczono czas oczekiwania."); }
        catch (HttpRequestException) { return Error("Unavailable", "NBP EWIB: błąd połączenia."); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or InvalidDataException or KeyNotFoundException)
        { return Error("InvalidResponse", "NBP EWIB: niepoprawna odpowiedź; nie uzupełniono danych."); }
    }
}
