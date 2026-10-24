namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool passwordPolicyScreen;
    private PasswordPolicyEditor? passwordPolicyEditor;
    private PasswordPolicyHistoryRow[] passwordPolicyHistory = [];
    private int policyHistoryOffset;
    private bool policyHistoryLoaded;
    public bool IsPasswordPolicyScreen => session != null && passwordPolicyScreen;
    public PasswordPolicyEditor? PasswordPolicyEditor => passwordPolicyEditor;
    public PasswordPolicyHistoryRow[] PasswordPolicyHistory => passwordPolicyHistory;
    public bool HasPasswordPolicy => passwordPolicyEditor != null;
    public bool HasPolicyHistory => policyHistoryLoaded;
    public bool IsPolicyHistoryEmpty => policyHistoryLoaded && passwordPolicyHistory.Length == 0;
    public bool CanSavePasswordPolicy => IsPasswordPolicyScreen && IsInstallationAdministrator && !busy && passwordPolicyEditor is { IsDirty: true, IsValid: true };
    public bool PolicyHistoryHasNext { get; private set; }
    public bool PolicyHistoryHasPrevious => policyHistoryOffset > 0;
    public Task OpenPasswordPolicy()
    {
        if (!IsInstallationAdministrator || busy || IsOwnPasswordChange) return Task.CompletedTask;
        return Run(async () =>
        {
            gusSettings = false; passwordPolicyScreen = true;
            passwordPolicyHistory = []; policyHistoryLoaded = false; policyHistoryOffset = 0;
            await ReadPasswordPolicy(); await ReadPolicyHistory(0);
        });
    }
    private async Task ReadPasswordPolicy()
    {
        passwordPolicyEditor = null; ChangedAll();
        AcceptPasswordPolicy(new(await api!.PasswordPolicy()));
    }
    private void AcceptPasswordPolicy(PasswordPolicyEditor value)
    {
        passwordPolicyEditor = value;
        value.PropertyChanged += (_, _) => { Changed(nameof(CanSavePasswordPolicy)); Changed(nameof(HasUnsavedChanges)); };
    }
    public Task SavePasswordPolicy(bool confirmDisable) => Run(async () =>
    {
        if (!IsPasswordPolicyScreen || passwordPolicyEditor is not { IsDirty: true, IsValid: true } || api == null) return;
        await api.SavePasswordPolicy(passwordPolicyEditor.Request(confirmDisable));
        await ReadPasswordPolicy();
        await ReadPolicyHistory(0); message = "Zapisano politykę haseł. Złożoność, historia i ważność są egzekwowane niezależnie przez serwer.";
    });
    public Task RefreshPasswordPolicy()
    {
        if (busy || !IsPasswordPolicyScreen) return Task.CompletedTask;
        Task Refresh() => Run(async () => { passwordPolicyHistory = []; policyHistoryLoaded = false; await ReadPasswordPolicy(); await ReadPolicyHistory(0); });
        if (passwordPolicyEditor?.IsDirty == true) { RequestDiscard(() => _ = Refresh()); return Task.CompletedTask; }
        return Refresh();
    }
    private async Task ReadPolicyHistory(int offset)
    {
        passwordPolicyHistory = []; policyHistoryLoaded = false; PolicyHistoryHasNext = false;
        var rows = await api!.PasswordPolicyHistory(offset);
        passwordPolicyHistory = rows.Take(50).Select(x => new PasswordPolicyHistoryRow(x)).ToArray();
        PolicyHistoryHasNext = rows.Length > 50; policyHistoryOffset = offset; policyHistoryLoaded = true;
    }
    public Task LoadPolicyHistory(int direction) => Run(() => ReadPolicyHistory(Math.Max(0, policyHistoryOffset + direction * 50)));
    public void ClosePasswordPolicy()
    {
        if (busy) return;
        void Close() { ResetPasswordPolicy(); gusSettings = true; ChangedAll(); }
        if (passwordPolicyEditor?.IsDirty == true) RequestDiscard(Close); else Close();
    }
    private void ResetPasswordPolicy() { passwordPolicyScreen = false; passwordPolicyEditor = null; passwordPolicyHistory = []; policyHistoryOffset = 0; policyHistoryLoaded = false; PolicyHistoryHasNext = false; }
}
