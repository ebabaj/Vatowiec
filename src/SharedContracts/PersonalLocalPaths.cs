namespace Vatowiec.SharedContracts;

public static class PersonalLocalPaths
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec", "Personal");
    public static string ValidateRoot(string root)
    {
        var full = Path.GetFullPath(root);
        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec");
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(parent) || !string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase) ||
            !(name == "Personal" || (name.StartsWith("Personal.WizardTest.", StringComparison.Ordinal) && Guid.TryParseExact(name[20..], "N", out _))))
            throw new ArgumentException("Niedozwolony katalog instalacji Personal.");
        for (var dir = new DirectoryInfo(full); dir != null && dir.FullName.Length >= parent.Length; dir = dir.Parent)
            if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new ArgumentException("Katalog Personal nie może być dowiązaniem.");
        return full;
    }
    public static string VerificationDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec", "Verification", "ADR-039");
}
