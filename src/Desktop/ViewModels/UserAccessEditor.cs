using System.ComponentModel;
using System.Runtime.CompilerServices;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed class PermissionChoice(PermissionDefinition definition) : INotifyPropertyChanged
{
    private bool selected;
    public string Code => definition.Code;
    public string Name => definition.Name;
    public string Description => definition.Description;
    public string[] Requires => definition.Requires;
    public bool IsSelected { get => selected; set { if (value == selected) return; selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class CompanyAccessEditor : INotifyPropertyChanged
{
    private bool hasAccess, updating;
    private readonly string[] original;
    public CompanyAccessEditor(AdministrationCompany company, PermissionDefinition[] definitions, UserCompanyAccess? access)
    {
        CompanyId = company.CompanyId; Name = company.Name; RowVersion = access?.RowVersion;
        original = access?.Permissions.Order(StringComparer.Ordinal).ToArray() ?? [];
        hasAccess = access != null;
        Permissions = definitions.Select(x => new PermissionChoice(x) { IsSelected = original.Contains(x.Code, StringComparer.Ordinal) }).ToArray();
        foreach (var permission in Permissions) permission.PropertyChanged += (_, _) => ChangedPermission(permission);
    }
    public Guid CompanyId { get; }
    public string Name { get; }
    public string? RowVersion { get; }
    public PermissionChoice[] Permissions { get; }
    public bool HasAccess
    {
        get => hasAccess;
        set
        {
            if (value == hasAccess) return;
            hasAccess = value;
            if (value && !Permissions.Any(x => x.IsSelected)) Permissions.SingleOrDefault(x => x.Code == "ViewProfile")!.IsSelected = true;
            Notify();
        }
    }
    public string[] Codes => hasAccess ? Permissions.Where(x => x.IsSelected).Select(x => x.Code).Order(StringComparer.Ordinal).ToArray() : [];
    public bool IsDirty => !original.SequenceEqual(Codes);
    public UserAccessItem Request() => new(CompanyId, Codes, RowVersion);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? property = null)
    { PropertyChanged?.Invoke(this, new(property)); PropertyChanged?.Invoke(this, new(nameof(IsDirty))); }
    private void ChangedPermission(PermissionChoice changed)
    {
        if (updating) return;
        updating = true;
        if (changed.IsSelected)
        {
            hasAccess = true;
            // Repeat to include transitive prerequisites supplied by the server catalog.
            for (var i = 0; i < Permissions.Length; i++)
                foreach (var item in Permissions.Where(x => x.IsSelected))
                    foreach (var dependency in item.Requires) { var required = Permissions.SingleOrDefault(x => x.Code == dependency); if (required != null) required.IsSelected = true; }
        }
        else
        {
            for (var i = 0; i < Permissions.Length; i++)
                foreach (var item in Permissions.Where(x => x.IsSelected))
                    if (item.Requires.Any(required => !Permissions.Any(x => x.Code == required && x.IsSelected))) item.IsSelected = false;
            if (!Permissions.Any(x => x.IsSelected)) hasAccess = false;
        }
        updating = false; Notify(nameof(HasAccess));
    }
}
