using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace Vatowiec.Core.Infrastructure.Identity;

public static class TemporaryPasswordGenerator
{
    public const int DefaultLength = 16;
    // No I/i, L/l, O/o, 0 or 1. Spaces are presentation separators only.
    private const string Upper = "ABCDEFGHJKMNPQRSTUVWXYZ", Lower = "abcdefghjkmnpqrstuvwxyz",
        Digits = "23456789", Symbols = "!@#$%&*?";
    private const string Alphabet = Upper + Lower + Digits + Symbols;

    public static string Generate(PasswordOptions policy)
    {
        var length = Math.Max(DefaultLength, Math.Max(policy.RequiredLength, policy.RequiredUniqueChars));
        if (length > 1024 || policy.RequiredUniqueChars > Alphabet.Length)
            throw new ArgumentException("Polityka haseł wymaga znaków lub długości niedostępnych dla generatora. Wpisz własne hasło tymczasowe.");
        var characters = new char[length];
        try
        {
            var count = 0;
            if (policy.RequireUppercase) characters[count++] = Pick(Upper);
            if (policy.RequireLowercase) characters[count++] = Pick(Lower);
            if (policy.RequireDigit) characters[count++] = Pick(Digits);
            if (policy.RequireNonAlphanumeric) characters[count++] = Pick(Symbols);
            while (count < length)
            {
                var candidate = Pick(Alphabet);
                if (count < policy.RequiredUniqueChars && characters.AsSpan(0, count).Contains(candidate)) continue;
                characters[count++] = candidate;
            }
            for (var i = characters.Length - 1; i > 0; i--)
            {
                var other = RandomNumberGenerator.GetInt32(i + 1);
                (characters[i], characters[other]) = (characters[other], characters[i]);
            }
            return new string(characters);
        }
        finally { Array.Clear(characters); }
    }
    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
