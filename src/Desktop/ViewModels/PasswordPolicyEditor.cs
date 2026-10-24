using System.ComponentModel;
using System.Runtime.CompilerServices;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed class PasswordPolicyEditor : INotifyPropertyChanged
{
    private readonly PasswordPolicyResponse original;
    private bool enabled, upper, lower, digit, special;
    private int minimum;
    private bool history, expiration;
    private int previousCount, validityDays;
    public PasswordPolicyEditor(PasswordPolicyResponse value)
    { original = value; enabled = value.IsEnabled; minimum = value.MinimumLength; upper = value.RequireUppercase; lower = value.RequireLowercase; digit = value.RequireDigit; special = value.RequireSpecialCharacter;
        history = value.PreventPasswordReuse; previousCount = value.PreviousPasswordCount; expiration = value.EnforcePasswordExpiration; validityDays = value.PasswordValidityDays; }
    public bool IsEnabled { get => enabled; set { enabled = value; Notify(); } }
    public int MinimumLength { get => minimum; set { minimum = value; Notify(); } }
    public bool RequireUppercase { get => upper; set { upper = value; Notify(); } }
    public bool RequireLowercase { get => lower; set { lower = value; Notify(); } }
    public bool RequireDigit { get => digit; set { digit = value; Notify(); } }
    public bool RequireSpecialCharacter { get => special; set { special = value; Notify(); } }
    public bool PreventPasswordReuse { get => history; set { history = value; Notify(); } }
    public int PreviousPasswordCount { get => previousCount; set { previousCount = value; Notify(); } }
    public bool EnforcePasswordExpiration { get => expiration; set { expiration = value; Notify(); } }
    public int PasswordValidityDays { get => validityDays; set { validityDays = value; Notify(); } }
    public bool IsDirty => IsEnabled != original.IsEnabled || MinimumLength != original.MinimumLength || upper != original.RequireUppercase || lower != original.RequireLowercase || digit != original.RequireDigit || special != original.RequireSpecialCharacter
        || history != original.PreventPasswordReuse || previousCount != original.PreviousPasswordCount || expiration != original.EnforcePasswordExpiration || validityDays != original.PasswordValidityDays;
    public bool IsValid => minimum is >= 1 and <= 1024 && previousCount is >= 1 and <= 20 && validityDays is >= 1 and <= 3650 && !string.IsNullOrEmpty(original.RowVersion);
    public PasswordPolicyRequest Request(bool confirmDisable) => new(IsEnabled, MinimumLength, upper, lower, digit, special, original.RowVersion, confirmDisable, history, previousCount, expiration, validityDays);
    public string Status => !IsValid ? "Sprawdź zakresy: długość 1–1024, historia 1–20, ważność 1–3650 dni i aktualną wersję konfiguracji." : IsDirty ? "Niezapisane zmiany polityki haseł" : "Polityka haseł jest zapisana";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? property = null)
    { PropertyChanged?.Invoke(this, new(property)); PropertyChanged?.Invoke(this, new(nameof(IsDirty))); PropertyChanged?.Invoke(this, new(nameof(IsValid))); PropertyChanged?.Invoke(this, new(nameof(Status))); }
}
public sealed class PasswordPolicyHistoryRow(PasswordPolicyHistory value)
{
    public string Time => value.OccurredAtUtc.ToLocalTime().ToString("g");
    public string Actor => "Wykonawca: " + value.ActorName;
    public string Before => "Przed: " + Summary(value.Before);
    public string After => "Po: " + Summary(value.After);
    private static string Summary(PasswordPolicyResponse p) => (!p.IsEnabled ? "złożoność wyłączona; dowolne niepuste hasło" :
        $"minimum {p.MinimumLength} znaków; wielka litera: {Yes(p.RequireUppercase)}, mała: {Yes(p.RequireLowercase)}, cyfra: {Yes(p.RequireDigit)}, znak specjalny: {Yes(p.RequireSpecialCharacter)}") +
        (p.PreventPasswordReuse ? $"; historia: aktualne i {p.PreviousPasswordCount} poprzednich" : "; historia: wyłączona") +
        (p.EnforcePasswordExpiration ? $"; ważność: {p.PasswordValidityDays} dni" : "; ważność: wyłączona");
    private static string Yes(bool value) => value ? "tak" : "nie";
}
