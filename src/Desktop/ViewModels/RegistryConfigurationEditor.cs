using System.ComponentModel;
using System.Runtime.CompilerServices;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed class RegistryConfigurationEditor(RegistryConfigurationResponse value) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Source => value.Source;
    public string Name => Source == "GUS" ? "GUS BIR1.2" : Source == "MF" ? "MF — wykaz VAT i WL2" : "VIES";
    public bool IsMf => Source == "MF";
    public bool IsGus => Source == "GUS";
    private int selectedEnvironment = value.Environment == "Production" ? 1 : 0;
    private bool confirmProduction;
    public int SelectedEnvironment { get => selectedEnvironment; set { selectedEnvironment = value; confirmProduction = false; Changed(); Changed(nameof(IsProduction)); Changed(nameof(ConfirmProduction)); } }
    public bool IsProduction => selectedEnvironment == 1;
    public bool ConfirmProduction { get => confirmProduction; set { confirmProduction = value; Changed(); } }
    public string TestUrl { get; set; } = value.TestUrl;
    public string ProductionUrl { get; set; } = value.ProductionUrl;
    public string? TestWl2Url { get; set; } = value.TestWl2Url;
    public string? ProductionWl2Url { get; set; } = value.ProductionWl2Url;
    public RegistryConfigurationRequest Request() => new(IsProduction ? "Production" : "Test", TestUrl, ProductionUrl,
        IsMf ? TestWl2Url : null, IsMf ? ProductionWl2Url : null, value.RowVersion, ConfirmProduction);
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
