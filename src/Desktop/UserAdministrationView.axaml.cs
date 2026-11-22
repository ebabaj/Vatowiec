using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop;

public partial class UserAdministrationView : UserControl
{
    public UserAdministrationView() => InitializeComponent();
    private MainViewModel Model => (MainViewModel)DataContext!;
    private void Run(Func<Task> action) => UiActions.Run(action, error => Model.ReportUnexpectedError(error));
    private Window Owner => (Window)TopLevel.GetTopLevel(this)!;
    private void UserSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || Model.IsBusy || e.AddedItems.OfType<UserSummary>().FirstOrDefault() is not { } clicked) return;
        Model.SelectedAdminUser = clicked;
        // Restore the visible row when the dirty-draft guard defers/rejects a switch.
        list.SetCurrentValue(Avalonia.Controls.Primitives.SelectingItemsControl.SelectedItemProperty, Model.SelectedAdminUser);
    }
    private void SearchClick(object? s, RoutedEventArgs e) => Run(() => Model.SearchAdminUsers());
    private void SearchKeyDown(object? s, KeyEventArgs e) => Run(async () => { if (e.Key == Key.Enter) { e.Handled = true; await Model.SearchAdminUsers(); } });
    private void PreviousClick(object? s, RoutedEventArgs e) => Run(() => Model.SearchAdminUsers(-1));
    private void NextClick(object? s, RoutedEventArgs e) => Run(() => Model.SearchAdminUsers(1));
    private void BackClick(object? s, RoutedEventArgs e) => Model.CloseUsersSettings();
    private void RefreshClick(object? s, RoutedEventArgs e) => Run(() => Model.RefreshAdminUser());
    private void SaveClick(object? s, RoutedEventArgs e) => Run(() => Model.SaveAdminUserAccess());
    private void HistoryClick(object? s, RoutedEventArgs e) => Run(() => Model.LoadAdminHistory());
    private void HistoryPreviousClick(object? s, RoutedEventArgs e) => Run(() => Model.LoadAdminHistory(-1));
    private void HistoryNextClick(object? s, RoutedEventArgs e) => Run(() => Model.LoadAdminHistory(1));
    private void CreateClick(object? s, RoutedEventArgs e) => Run(async () =>
    {
        var choice = await AdministrationDialogs.ChooseTemporaryPassword(Owner, "Utwórz konto", "Nowe konto: „" + Model.NewUserName + "”.");
        if (choice == null) return;
        var result = await Model.CreateAdminUser(choice.Password);
        if (result != null) await AdministrationDialogs.TemporaryPassword(Owner, result.User.User.UserName, result.TemporaryPassword, result.IsGenerated);
    });
    private void ResetClick(object? s, RoutedEventArgs e) => Run(async () =>
    {
        var choice = await AdministrationDialogs.ChooseTemporaryPassword(Owner, "Resetuj hasło", "Zresetować hasło konta „" + Model.AdminAccountName + "”? Dotychczasowe sesje przestaną działać.");
        if (choice == null) return;
        var result = await Model.ResetAdminPassword(choice.Password);
        if (choice.Password == null && result is { IsGenerated: true })
            await AdministrationDialogs.TemporaryPassword(Owner, result.User.User.UserName, result.TemporaryPassword, result.IsGenerated);
    });
    private void BlockClick(object? s, RoutedEventArgs e) => Run(async () =>
    {
        if (await AdministrationDialogs.Confirm(Owner, Model.AdminBlockAction + " Konto: „" + Model.AdminAccountName + "”. Dotychczasowe sesje zostaną unieważnione.")) await Model.ToggleAdminBlock();
    });
    private void ArchiveClick(object? s, RoutedEventArgs e) => Run(async () =>
    {
        if (await AdministrationDialogs.Confirm(Owner, "Usunąć logicznie użytkownika „" + Model.AdminAccountName + "”? Wszystkie sesje, role i prawa do firm zostaną wyłączone. Tożsamość i historia pozostaną w archiwum."))
            await Model.ArchiveAdminUser();
    });
    private async Task Restore(bool newPerson)
    {
        var action = newPerson ? "To nowa osoba — wykorzystaj login" : "To ta sama osoba — przywróć konto";
        var explanation = newPerson ? "Stara tożsamość pozostanie na zawsze w archiwum. Powstanie nowa osoba bez dawnych praw, haseł i sesji."
            : "Zachowasz oryginalną tożsamość i historię. Dawne uprawnienia nie zostaną przywrócone.";
        var choice = await AdministrationDialogs.ChooseTemporaryPassword(Owner, action, "Konto: „" + Model.AdminAccountName + "”. " + explanation);
        if (choice == null) return;
        var result = await Model.RestoreAdminUser(newPerson, choice.Password);
        if (choice.Password == null && result is { IsGenerated: true })
            await AdministrationDialogs.TemporaryPassword(Owner, result.User.User.DisplayName, result.TemporaryPassword);
    }
    private void RestoreSameClick(object? s, RoutedEventArgs e) => Run(() => Restore(false));
    private void ReuseLoginClick(object? s, RoutedEventArgs e) => Run(() => Restore(true));
    private void RoleClick(object? s, RoutedEventArgs e) => Run(async () =>
    {
        var text = Model.AdministratorRole ? "Nadać rolę administratora instalacji" : "Odebrać rolę administratora instalacji";
        if (await AdministrationDialogs.Confirm(Owner, text + " użytkownikowi „" + Model.AdminAccountName + "”? Jego dotychczasowe sesje zostaną unieważnione. Dostęp do firm pozostanie bez zmian."))
            await Model.SaveAdministratorRole();
    });
}
