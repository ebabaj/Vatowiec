using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence;

// Names/codes only, no rates. NBP A/B reference lists, 2026-10-22 (ADR-023).
// ISO codes are the sole currency keys. Missing publications do not mean inactivity.
public static class CurrencySeed
{
    public static Currency[] Rows() => Data.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line =>
        {
            var parts = line.Split('|');
            return new Currency { Code = parts[0], Name = parts[1], DownloadA = parts[0] is "EUR" or "USD" or "GBP" or "CHF" };
        }).ToArray();
    private const string Data = """
        PLN|złoty polski
        THB|bat tajlandzki
        USD|dolar amerykański
        AUD|dolar australijski
        HKD|dolar Hongkongu
        CAD|dolar kanadyjski
        NZD|dolar nowozelandzki
        SGD|dolar singapurski
        EUR|euro
        HUF|forint węgierski
        CHF|frank szwajcarski
        GBP|funt szterling
        UAH|hrywna ukraińska
        JPY|jen japoński
        CZK|korona czeska
        DKK|korona duńska
        ISK|korona islandzka
        NOK|korona norweska
        SEK|korona szwedzka
        RON|lej rumuński
        TRY|lira turecka
        ILS|szekel izraelski
        CLP|peso chilijskie
        PHP|peso filipińskie
        MXN|peso meksykańskie
        ZAR|rand południowoafrykański
        BRL|real brazylijski
        MYR|ringgit malezyjski
        IDR|rupia indonezyjska
        INR|rupia indyjska
        KRW|won południowokoreański
        CNY|juan chiński
        XDR|SDR — jednostka rozrachunkowa MFW
        AFN|afgani afgańskie
        MGA|ariary malgaski
        PAB|balboa panamska
        ETB|birr etiopski
        VES|boliwar wenezuelski
        BOB|boliwiano boliwijskie
        CRC|colon kostarykański
        SVC|colon salwadorski
        NIO|cordoba nikaraguańska
        GMD|dalasi gambijskie
        MKD|denar macedoński
        DZD|dinar algierski
        BHD|dinar bahrajski
        IQD|dinar iracki
        JOD|dinar jordański
        KWD|dinar kuwejcki
        LYD|dinar libijski
        RSD|dinar serbski
        TND|dinar tunezyjski
        MAD|dirham marokański
        AED|dirham Zjednoczonych Emiratów Arabskich
        STN|dobra Wysp Świętego Tomasza i Książęcej
        BSD|dolar bahamski
        BBD|dolar barbadoski
        BZD|dolar belizeński
        BND|dolar brunejski
        FJD|dolar Fidżi
        GYD|dolar gujański
        JMD|dolar jamajski
        LRD|dolar liberyjski
        NAD|dolar namibijski
        SRD|dolar surinamski
        TTD|dolar Trynidadu i Tobago
        XCD|dolar wschodniokaraibski
        SBD|dolar Wysp Salomona
        VND|dong wietnamski
        AMD|dram armeński
        CVE|escudo Zielonego Przylądka
        AWG|florin arubański
        BIF|frank burundyjski
        XOF|frank CFA BCEAO
        XAF|frank CFA BEAC
        XPF|frank CFP
        DJF|frank Dżibuti
        GNF|frank gwinejski
        KMF|frank Komorów
        CDF|frank kongijski
        RWF|frank rwandyjski
        EGP|funt egipski
        GIP|funt gibraltarski
        LBP|funt libański
        SSP|funt południowosudański
        SDG|funt sudański
        SYP|funt syryjski
        GHS|cedi ghański
        HTG|gourde haitańskie
        PYG|guarani paragwajskie
        XCG|gulden karaibski
        PGK|kina papuaska
        LAK|kip laotański
        MWK|kwacha malawijska
        ZMW|kwacha zambijska
        AOA|kwanza angolska
        MMK|kiat birmański
        GEL|lari gruzińskie
        MDL|lej mołdawski
        ALL|lek albański
        HNL|lempira honduraska
        SLE|leone Sierra Leone
        SZL|lilangeni Eswatini
        LSL|loti Lesotho
        AZN|manat azerbejdżański
        MZN|metical mozambicki
        NGN|naira nigeryjska
        ERN|nakfa erytrejska
        TWD|dolar tajwański
        TMT|manat turkmeński
        MRU|ouguiya mauretańska
        TOP|paʻanga tongijska
        MOP|pataca Makau
        ARS|peso argentyńskie
        DOP|peso dominikańskie
        COP|peso kolumbijskie
        CUP|peso kubańskie
        UYU|peso urugwajskie
        BWP|pula botswańska
        GTQ|quetzal gwatemalski
        IRR|rial irański
        YER|rial jemeński
        QAR|rial katarski
        OMR|rial omański
        SAR|rial saudyjski
        KHR|riel kambodżański
        BYN|rubel białoruski
        RUB|rubel rosyjski
        LKR|rupia lankijska
        MVR|rupia malediwska
        MUR|rupia Mauritiusu
        NPR|rupia nepalska
        PKR|rupia pakistańska
        SCR|rupia seszelska
        PEN|sol peruwiański
        KGS|som kirgiski
        TJS|somoni tadżyckie
        UZS|sum uzbecki
        KES|szyling kenijski
        SOS|szyling somalijski
        TZS|szyling tanzański
        UGX|szyling ugandyjski
        BDT|taka bengalska
        WST|tala samoańska
        KZT|tenge kazachskie
        MNT|tugrik mongolski
        VUV|vatu Vanuatu
        BAM|marka zamienna Bośni i Hercegowiny
        ZWG|złoto Zimbabwe
        """;
}
