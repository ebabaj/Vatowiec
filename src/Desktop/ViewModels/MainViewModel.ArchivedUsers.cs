using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public Task ArchiveAdminUser() => Run(async () =>
    {
        if (api == null || adminUser == null || !IsUsersSettings || IsArchivedAdminUser) return;
        if (HasAdminChanges) throw new ArgumentException("Zapisz lub porzuć szkic przed usunięciem użytkownika.");
        AcceptAdminUser(await api.ArchiveUser(adminUser.User.Id, new(adminUser.ConcurrencyStamp, true)));
        if (!showArchivedUsers) await ReadAdminUsers(0);
        await ReadAdminHistory(0); message = "Usunięto użytkownika. Sesje i uprawnienia wyłączono; tożsamość i historia pozostają w archiwum.";
    });
    public async Task<TemporaryPasswordResponse?> RestoreAdminUser(bool newPerson, string? password)
    {
        TemporaryPasswordResponse? result = null;
        if (!CanRestoreAdminUser || adminUser == null) return null;
        if (HasAdminChanges) { message = "Zapisz lub porzuć szkic przed przywróceniem lub wykorzystaniem loginu."; ChangedAll(); return null; }
        await Run(async () =>
        {
            var response = await api!.RestoreUser(adminUser.User.Id, new(adminUser.ConcurrencyStamp, true, password), newPerson);
            if (newPerson) selectedAdminUser = null;
            AcceptAdminUser(response.User); result = response; await ReadAdminHistory(0);
            message = newPerson ? "Utworzono konto nowej osoby. Stara tożsamość pozostaje w archiwum. Nadaj świadomie nowe uprawnienia."
                : "Przywrócono tę samą osobę. Przy logowaniu wymagana jest zmiana hasła. Nadaj ponownie uprawnienia i role.";
        });
        return result;
    }
}
