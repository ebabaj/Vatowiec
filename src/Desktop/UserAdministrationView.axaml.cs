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
    private Window Owner => (Window)TopLevel.GetTopLevel(this)!;
    private void UserSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || Model.IsBusy || e.AddedItems.OfType<UserSummary>().FirstOrDefault() is not { } clicked) return;
        Model.SelectedAdminUser = clicked;
        // Restore the visible row when the dirty-draft guard defers/rejects a switch.
        list.SetCurrentValue(Avalonia.Controls.Primitives.SelectingItemsControl.SelectedItemProperty, Model.SelectedAdminUser);
    }
    private async void SearchClick(object? s, RoutedEventArgs e) => await Model.SearchAdminUsers();
    private async void SearchKeyDown(object? s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await Model.SearchAdminUsers(); } }
    private async void PreviousClick(object? s, RoutedEventArgs e) => await Model.SearchAdminUsers(-1);
    private async void NextClick(object? s, RoutedEventArgs e) => await Model.SearchAdminUsers(1);
    private void BackClick(object? s, RoutedEventArgs e) => Model.CloseUsersSettings();
    private async void RefreshClick(object? s, RoutedEventArgs e) => await Model.RefreshAdminUser();
    private async void SaveClick(object? s, RoutedEventArgs e) => await Model.SaveAdminUserAccess();
    private async void HistoryClick(object? s, RoutedEventArgs e) => await Model.LoadAdminHistory();
    private async void HistoryPreviousClick(object? s, RoutedEventArgs e) => await Model.LoadAdminHistory(-1);
    private async void HistoryNextClick(object? s, RoutedEventArgs e) => await Model.LoadAdminHistory(1);
    private async void CreateClick(object? s, RoutedEventArgs e)
    {
        var choice = await AdministrationDialogs.ChooseTemporaryPassword(Owner, "Utwórz konto", "Nowe konto: „" + Model.NewUserName + "”.");
        if (choice == null) return;
        var result = await Model.CreateAdminUser(choice.Password);
        if (result != null) await AdministrationDialogs.TemporaryPassword(Owner, result.User.User.UserName, result.TemporaryPassword, result.IsGenerated);
    }
    private async void ResetClick(object? s, RoutedEventArgs e)
    {
        var choice = await AdministrationDialogs.ChooseTemporaryPassword(Owner, "Resetuj hasło", "Zresetować hasło konta „" + Model.AdminAccountName + "”? Dotychczasowe sesje przestaną działać.");
        if (choice == null) return;
        var result = await Model.ResetAdminPassword(choice.Password);
        if (choice.Password == null && result is { IsGenerated: true })
            await AdministrationDialogs.TemporaryPassword(Owner, result.User.User.UserName, result.TemporaryPassword, result.IsGenerated);
    }
    private async void BlockClick(object? s, RoutedEventArgs e)
    {
        if (await AdministrationDialogs.Confirm(Owner, Model.AdminBlockAction + " Konto: „" + Model.AdminAccountName + "”. Dotychczasowe sesje zostaną unieważnione.")) await Model.ToggleAdminBlock();
    }
    private async void RoleClick(object? s, RoutedEventArgs e)
    {
        var text = Model.AdministratorRole ? "Nadać rolę administratora instalacji" : "Odebrać rolę administratora instalacji";
        if (await AdministrationDialogs.Confirm(Owner, text + " użytkownikowi „" + Model.AdminAccountName + "”? Jego dotychczasowe sesje zostaną unieważnione. Dostęp do firm pozostanie bez zmian."))
            await Model.SaveAdministratorRole();
    }
}
