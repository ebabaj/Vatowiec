using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts.ServerTls;

namespace Vatowiec.Desktop;

// ADR-047: Ustawienia instalacji → Certyfikat serwera.
public partial class ServerCertificateView : UserControl
{
    public ServerCertificateView() => InitializeComponent();
    private MainViewModel Model => (MainViewModel)DataContext!;
    private Window Owner => (Window)TopLevel.GetTopLevel(this)!;
    private void Run(Func<Task> action) => UiActions.Run(action, error => Model.ReportUnexpectedError(error));

    private void SaveClick(object? s, RoutedEventArgs e) => Run(() => Model.SaveServerCertificate());
    private void RefreshClick(object? s, RoutedEventArgs e) => Run(() => Model.RefreshServerCertificate());
    private void BackClick(object? s, RoutedEventArgs e) => Model.CloseServerCertificate();

    private void ReplaceCaClick(object? s, RoutedEventArgs e) => Run(async () =>
    {
        if (await AdministrationDialogs.Confirm(Owner,
            "Wymienić CA instalacji? Po wymianie żadna stacja nie połączy się z serwerem, dopóki nie otrzyma nowego kodu albo pliku połączenia. " +
            "Używaj tego przy podejrzeniu wycieku klucza (np. kradzież serwera) albo przed końcem ważności CA."))
            await Model.RequestCaReplacement();
    });

    private void SaveConnectionFileClick(object? s, RoutedEventArgs e) => Run(async () =>
    {
        var text = await Model.ServerConnectionFileText();
        if (text == null) return;
        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz plik połączenia VATowiec",
            SuggestedFileName = "VATowiec" + ConnectionFile.Extension,
            DefaultExtension = ConnectionFile.Extension.TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType("Plik połączenia VATowiec") { Patterns = ["*" + ConnectionFile.Extension] }]
        });
        if (file == null) return;
        await using (var stream = await file.OpenWriteAsync())
        await using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            await writer.WriteAsync(text);
        Model.ReportServerConnectionFileSaved(file.Name);
    });
}
