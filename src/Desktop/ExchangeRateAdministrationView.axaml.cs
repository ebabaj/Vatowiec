using Avalonia.Controls;
using Avalonia.Interactivity;
using Vatowiec.Desktop.ViewModels;

namespace Vatowiec.Desktop;

// ADR-048: Ustawienia instalacji → Kursy NBP.
public partial class ExchangeRateAdministrationView : UserControl
{
    public ExchangeRateAdministrationView() => InitializeComponent();
    private MainViewModel Model => (MainViewModel)DataContext!;
    private void Run(Func<Task> action) => UiActions.Run(action, error => Model.ReportUnexpectedError(error));

    private void SaveClick(object? s, RoutedEventArgs e) => Run(() => Model.SaveExchangeRateHistoryStart());
    private void RunClick(object? s, RoutedEventArgs e) => Run(() => Model.RequestExchangeRateRun());
    private void RefreshClick(object? s, RoutedEventArgs e) => Run(() => Model.RefreshExchangeRateAdministration());
    private void KeepClick(object? s, RoutedEventArgs e) => Run(() => Model.ResolveExchangeRateConflict(false));
    private void AcceptClick(object? s, RoutedEventArgs e) => Run(() => Model.ResolveExchangeRateConflict(true));
    private void NextHistoryClick(object? s, RoutedEventArgs e) => Run(() => Model.NextExchangeRateHistory());
    private void PreviousHistoryClick(object? s, RoutedEventArgs e) => Run(() => Model.PreviousExchangeRateHistory());
    private void BackClick(object? s, RoutedEventArgs e) => Model.CloseExchangeRateAdministration();
}
