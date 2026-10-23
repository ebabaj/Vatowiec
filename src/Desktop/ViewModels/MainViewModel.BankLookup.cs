using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private string? proposedBankName;
    private string[] bankBicChoices = [];
    private string? selectedBankBic;
    public string BankLookupMessage { get; private set; } = "";
    public bool HasBankNameProposal => proposedBankName is not null;
    public string BankNameProposal => "NBP EWIB proponuje nazwę: " + proposedBankName;
    public string[] BankBicChoices => bankBicChoices;
    public bool HasBicProposal => bankBicChoices.Length != 0;
    public string? SelectedBankBic
    {
        get => selectedBankBic;
        set { selectedBankBic = value; Changed(); Changed(nameof(CanApplyBankBic)); }
    }
    public bool CanApplyBankBic => selectedBankBic is not null && bankBicChoices.Contains(selectedBankBic, StringComparer.Ordinal);

    private void PrepareBankProposals(BankAccountValidationResponse result)
    {
        ResetBankLookup();
        if (detailEditor is null) return;
        var bank = result.BankLookup;
        if (bank is null)
        {
            BankLookupMessage = result.Status == "ValidIban"
                ? "Dla rachunku zagranicznego uzupełnij nazwę banku i BIC ręcznie."
                : "Nie pobrano banku z EWIB — numer nie przeszedł kontroli IBAN/NRB.";
            return;
        }
        BankLookupMessage = bank.Message;
        if (bank.Status != "Found") return;
        if (!string.IsNullOrWhiteSpace(bank.BankName))
        {
            if (string.IsNullOrWhiteSpace(detailEditor.BankName)) detailEditor.BankName = bank.BankName;
            else if (!string.Equals(detailEditor.BankName.Trim(), bank.BankName, StringComparison.Ordinal)) proposedBankName = bank.BankName;
        }
        var bics = bank.Bics;
        if (bics.Length == 0) BankLookupMessage += ". NBP nie podał BIC; pole pozostawiono bez zmian.";
        else if (bics.Length == 1 && string.IsNullOrWhiteSpace(detailEditor.Bic)) detailEditor.Bic = bics[0];
        else if (bics.Length > 1 || !string.Equals(detailEditor.Bic?.Trim(), bics[0], StringComparison.OrdinalIgnoreCase))
        {
            bankBicChoices = bics;
            selectedBankBic = bics.Length == 1 ? bics[0] : null;
            BankLookupMessage += bics.Length > 1 ? ". Wybierz właściwy BIC albo pozostaw własne pole bez zmian." : ". Własny BIC pozostawiono bez zmian; możesz użyć propozycji.";
        }
    }
    private bool CanUseBankProposal => !busy && CanEditDetails && detailEditor is not null &&
        bankValidation?.NormalizedNumber == detailEditor.AccountNumber;
    public void ApplyBankNameProposal()
    {
        if (!CanUseBankProposal || proposedBankName is null) return;
        detailEditor!.BankName = proposedBankName;
        proposedBankName = null;
        ChangedAll();
    }
    public void ApplyBankBicProposal()
    {
        if (!CanUseBankProposal || !CanApplyBankBic) return;
        detailEditor!.Bic = selectedBankBic;
        bankBicChoices = [];
        selectedBankBic = null;
        ChangedAll();
    }
    private void ResetBankLookup()
    {
        proposedBankName = null;
        bankBicChoices = [];
        selectedBankBic = null;
        BankLookupMessage = "";
        foreach (var property in new[] { nameof(BankLookupMessage), nameof(HasBankNameProposal), nameof(BankNameProposal),
                     nameof(BankBicChoices), nameof(HasBicProposal), nameof(SelectedBankBic), nameof(CanApplyBankBic) }) Changed(property);
    }
}
