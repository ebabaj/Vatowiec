using System.ComponentModel;
using System.Runtime.CompilerServices;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed class PasswordPolicyEditor : INotifyPropertyChanged
{
    private readonly PasswordPolicyResponse original;
    private bool enabled, upper, lower, digit, special;
    private int minimum;
    public PasswordPolicyEditor(PasswordPolicyResponse value)
    { original = value; enabled = value.IsEnabled; minimum = value.MinimumLength; upper = value.RequireUppercase; lower = value.RequireLowercase; digit = value.RequireDigit; special = value.RequireSpecialCharacter; }
    public bool IsEnabled { get => enabled; set { enabled = value; Notify(); } }
    public int MinimumLength { get => minimum; set { minimum = value; Notify(); } }
    public bool RequireUppercase { get => upper; set { upper = value; Notify(); } }
    public bool RequireLowercase { get => lower; set { lower = value; Notify(); } }
    public bool RequireDigit { get => digit; set { digit = value; Notify(); } }
    public bool RequireSpecialCharacter { get => special; set { special = value; Notify(); } }
    public bool IsDirty => IsEnabled != original.IsEnabled || MinimumLength != original.MinimumLength || upper != original.RequireUppercase || lower != original.RequireLowercase || digit != original.RequireDigit || special != original.RequireSpecialCharacter;
    public bool IsValid => minimum is >= 1 and <= 1024 && !string.IsNullOrEmpty(original.RowVersion);
    public PasswordPolicyRequest Request(bool confirmDisable) => new(IsEnabled, MinimumLength, upper, lower, digit, special, original.RowVersion, confirmDisable);
    public string Status => !IsValid ? "Minimalna długość musi wynosić od 1 do 1024 znaków; konfiguracja wymaga odczytu aktualnej wersji z API." : IsDirty ? "Niezapisane zmiany polityki haseł" : "Polityka haseł jest zapisana";
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
    private static string Summary(PasswordPolicyResponse p) => !p.IsEnabled ? "złożoność wyłączona; dowolne niepuste hasło" :
        $"minimum {p.MinimumLength} znaków; wielka litera: {Yes(p.RequireUppercase)}, mała: {Yes(p.RequireLowercase)}, cyfra: {Yes(p.RequireDigit)}, znak specjalny: {Yes(p.RequireSpecialCharacter)}";
    private static string Yes(bool value) => value ? "tak" : "nie";
}
