using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.Services;

// No password, cookie or token belongs in these types or in the preferences file.
public sealed class LoginTarget
{
    public LoginTarget(string apiAddress, string userName, Guid? userId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiAddress);
        ArgumentException.ThrowIfNullOrEmpty(userName);
        ApiAddress = PersonalApiIdentity.TryParse(apiAddress.Trim(), out var installation)
            ? PersonalApiIdentity.For(installation) : VatowiecApiClient.ValidateAddress(apiAddress.Trim()).AbsoluteUri;
        UserName = userName.Trim();
        UserId = userId is { } id && id != Guid.Empty ? id : null;
        if (UserName.Length is 0 or > 256 || UserName.Contains('\0')) throw new ArgumentException("Wpisz poprawny login.");
        Key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { ApiAddress, UserName.ToUpperInvariant() }))));
    }
    public string ApiAddress { get; }
    public string UserName { get; }
    public Guid? UserId { get; }
    public string Key { get; }
    public string CredentialName => UserId is { } id ? "Vatowiec:DesktopLogin:v2:" + Key + ":" + id.ToString("N") : "Vatowiec:DesktopLogin:v1:" + Key;
}

public sealed record LoginProfilePreferences(string ApiAddress, string UserName, bool RememberCredentials = false,
    bool AutomaticLogin = false, Guid? DefaultCompanyId = null, Guid? UserId = null)
{
    [JsonIgnore] public LoginTarget Target => new(ApiAddress, UserName, UserId);
}
public sealed record LoginPreferences(int Version = 1, string? LastTargetKey = null, LoginProfilePreferences[]? Profiles = null)
{
    public LoginProfilePreferences? Find(LoginTarget target) => Profiles?.FirstOrDefault(x => x.Target.Key == target.Key);
    public LoginPreferences WithProfile(LoginProfilePreferences profile, bool makeLast = false) => this with
    {
        LastTargetKey = makeLast ? profile.Target.Key : LastTargetKey,
        Profiles = [.. (Profiles ?? []).Where(x => x.Target.Key != profile.Target.Key), profile]
    };
}

public sealed class LocalLoginStorageException(string message) : Exception(message);
public interface ILoginPreferencesStore
{
    LoginPreferences Load();
    void Save(LoginPreferences preferences);
}
public interface ILoginCredentialStore
{
    bool Contains(LoginTarget target);
    string? Read(LoginTarget target);
    void Write(LoginTarget target, string password);
    void Delete(LoginTarget target);
}
internal sealed class UnavailableLoginCredentialStore : ILoginCredentialStore
{
    private static LocalLoginStorageException Error() => new("Bezpieczny magazyn poświadczeń jest niedostępny. Użyj logowania ręcznego.");
    public bool Contains(LoginTarget target) => false;
    public string? Read(LoginTarget target) => throw Error();
    public void Write(LoginTarget target, string password) => throw Error();
    public void Delete(LoginTarget target) => throw Error();
}

// The default ViewModel is side-effect free; only the desktop composition uses disk/Windows.
public sealed class MemoryLoginPreferencesStore : ILoginPreferencesStore
{
    private LoginPreferences value = new();
    public LoginPreferences Load() => value;
    public void Save(LoginPreferences preferences) => value = preferences;
}

public sealed class FileLoginPreferencesStore(string? filePath = null) : ILoginPreferencesStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true };
    private readonly string path = filePath ?? DefaultPath();
    private static string DefaultPath()
    {
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.IsPathFullyQualified(directory) ? Path.Combine(directory, "Vatowiec", "Desktop", "login-preferences.json") : "";
    }
    private static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or ArgumentException or NotSupportedException;
    private static void Validate(LoginPreferences value)
    {
        if (value.Version != 1 || value.Profiles?.Length > 1000 || value.Profiles?.Any(x => x == null) == true) throw new JsonException();
        var keys = (value.Profiles ?? []).Select(x => x.Target.Key).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length ||
            (value.Profiles ?? []).Any(x => x.AutomaticLogin && !x.RememberCredentials)) throw new JsonException();
    }
    public LoginPreferences Load()
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException();
            if (!File.Exists(path)) return new();
            if (new FileInfo(path).Length > 262144) throw new JsonException();
            var value = JsonSerializer.Deserialize<LoginPreferences>(File.ReadAllText(path), Json) ?? throw new JsonException();
            Validate(value); return value;
        }
        catch (Exception e) when (Expected(e)) { throw new LocalLoginStorageException("Nie można odczytać lokalnych preferencji logowania. Dostępne jest logowanie ręczne."); }
    }
    public void Save(LoginPreferences preferences)
    {
        string? temporary = null;
        try
        {
            Validate(preferences);
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException();
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, "login-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Json), Encoding.UTF8);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (Expected(e)) { throw new LocalLoginStorageException("Nie można zapisać lokalnych preferencji logowania."); }
        finally
        {
            if (temporary != null)
                try { File.Delete(temporary); } catch (Exception e) when (Expected(e)) { /* Only a nonsecret temporary preference file. */ }
        }
    }
}
