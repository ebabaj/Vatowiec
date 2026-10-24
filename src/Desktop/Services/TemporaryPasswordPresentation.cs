namespace Vatowiec.Desktop.Services;

public static class TemporaryPasswordPresentation
{
    // Never use this display string as the login password or clipboard value.
    public static string Format(string password, bool generated) => generated
        ? string.Join(" ", password.Chunk(4).Select(group => new string(group))) : password;
}
