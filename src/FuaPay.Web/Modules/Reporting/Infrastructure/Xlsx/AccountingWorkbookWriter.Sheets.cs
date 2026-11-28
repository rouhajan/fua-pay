using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;

using FuaPay.Web.Modules.Reporting.Application;

namespace FuaPay.Web.Modules.Reporting.Infrastructure.Xlsx;

internal sealed partial class AccountingWorkbookWriter
{
    private void Card(OpenXmlWriter writer)
    {
        var incomplete = report.CardAmountsIncomplete;
        Row(writer, 4, 19, [incomplete
            ? "Neúplné karetní podklady. Součet zahrnuje jen doložené částky. Viz Kontroly."
            : "Lokální finanční události. Bankovní vypořádání neověřeno."], [2]);
        Header(writer, ["Datum a čas", "Operace", "Číslo dokladu", "ID platby ČSOB", "Platba (Kč)", "Storno (Kč)", "Čistě (Kč)"],
            4);
        for (var i = 0; i < report.Card.Length; i++)
        {
            var item = report.Card[i];
            var row = 6 + i;
            var alternate = i % 2 != 0;
            Row(writer, row, 21,
                [item.At, item.Operation, item.DocumentNumber, item.PayId, Money(item.Received),
                    Money(item.Returned), new Formula($"E{row}-F{row}", Money(item.Received - item.Returned))],
                alternate ? [35, 8, 36, 8, 37, 37, 38] : [31, 5, 32, 5, 33, 33, 34]);
            if (item.DocumentId.HasValue)
            {
                _cardLinks.Add(new Hyperlink { Reference = $"C{row}", Location = $"'Kontroly'!A{40 + i}" });
            }
        }

        var last = LastData(report.Card.Length);
        Row(writer, last + 1, 27, [incomplete ? "CELKEM DOLOŽENÉ" : "CELKEM", null, null, null,
            Sum("E", last, report.Received), Sum("F", last, report.Returned), Sum("G", last, report.NetCard)],
            [22, 22, 22, 22, 39, 39, 39]);
    }

    private void Summary(OpenXmlWriter writer)
    {
        var wallet = report.Card.Where(item => item.Wallet).Sum(item => item.Received);
        var direct = report.Received - wallet;
        var last = LastData(report.Card.Length);
        var cardIncomplete = report.CardAmountsIncomplete;
        var creditIncomplete = report.CreditBreakdownIncomplete;
        var model = report.FeeModelAvailable;
        var modelVariable = model
            ? Math.Round(Math.Max(0m, Money(report.NetCard)) * 0.01m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var warnings = new List<string>();
        if (cardIncomplete)
        {
            warnings.Add("NEÚPLNÉ KARETNÍ PODKLADY. Poplatky N/A.");
        }
        if (!report.LedgerComplete)
        {
            warnings.Add("NEÚPLNÁ HISTORIE KREDITU.");
        }
        else if (creditIncomplete)
        {
            warnings.Add("NEÚPLNÉ ČLENĚNÍ KREDITU.");
        }

        if (warnings.Count > 0)
        {
            Row(writer, 4, 23, [string.Join(" ", warnings) + " Viz Kontroly."], [10]);
            _summaryMerges.Add(new MergeCell { Reference = "A4:G4" });
        }

        Section(5, "PLATBY KARTOU");
        Metric(6, "Dobití kreditu kartou", new Formula($"SUMIF('Platby ČSOB'!B6:B{last},\"Dobití kreditu\",'Platby ČSOB'!E6:E{last})", Money(wallet)));
        Metric(7, "Přímé platby zakázek kartou", new Formula($"SUMIF('Platby ČSOB'!B6:B{last},\"Platba zakázky\",'Platby ČSOB'!E6:E{last})", Money(direct)));
        Metric(8, "Vrácení kartou", new Formula($"-'Platby ČSOB'!F{last + 1}", -Money(report.Returned)));
        Metric(9, cardIncomplete ? "ČISTÉ PLATBY KARTOU – DOLOŽENÁ ČÁST" : "ČISTÉ PLATBY KARTOU",
            new Formula("SUM(G6:G8)", Money(report.NetCard)), total: true);
        Section(10, "INTERNÍ MODEL POPLATKŮ ČSOB (NEOVĚŘENÉ)");
        Metric(11, "Modelový měsíční paušál ČSOB", model ? (object)99m : "N/A", valueStyle: 43);
        Metric(12, "Modelový poplatek z kladného čistého objemu",
            model ? new Formula("ROUND(MAX(0,G9)*F12,2)", modelVariable) : "N/A", rate: model ? 0.01m : null);
        Metric(13, "PO MODELOVÝCH POPLATCÍCH",
            model ? new Formula("G9-SUM(G11:G12)", Money(report.NetCard) - 99m - modelVariable) : "N/A", total: true);

        Section(15, creditIncomplete ? "KREDITNÍ ÚČTY – NEÚPLNÉ ČLENĚNÍ" : "KREDITNÍ ÚČTY");
        Metric(16, "Počáteční zůstatek", report.LedgerComplete ? (object)Money(report.Opening) : "Neúplná historie");
        Metric(17, "Dobití kreditu kartou", Money(report.MovementValue(AccountingMovementKind.CardTopUp)));
        Metric(18, "Vrácení karetního dobití", Money(report.MovementValue(AccountingMovementKind.CardTopUpReturn)));
        Metric(19, "Převody SafeQ (bez nového příjmu)", Money(report.MovementValue(AccountingMovementKind.SafeQ)));
        Metric(20, "Úhrady zakázek kreditem", Money(report.MovementValue(AccountingMovementKind.Job)));
        Metric(21, "Tisk hrazený kreditem", Money(report.MovementValue(AccountingMovementKind.Print)));
        var other = report.MovementValue(AccountingMovementKind.ManualTopUp) +
            report.MovementValue(AccountingMovementKind.JobReturn) + report.MovementValue(AccountingMovementKind.Adjustment) +
            report.MovementValue(AccountingMovementKind.Unknown);
        Metric(22, "Další kreditní pohyby", Money(other));
        Metric(23, "KREDIT CELKEM", report.LedgerComplete
            ? new Formula("SUM(G16:G22)", Money(checked(report.Opening + report.MovementTotal))) : "Neúplná historie", total: true);
        Metric(24, "Součet evidovaných zůstatků", report.LedgerComplete
            ? new Formula($"'Zůstatky'!D{LastData(report.Balances.Length) + 1}", Money(report.Closing)) : "Neúplná historie");
        Section(26, "ZAKÁZKY");
        Metric(27, "Počet zakázek v exportu", report.Jobs.Length, valueStyle: 45);
        Metric(28, "Uhrazené zakázky v exportu", report.Jobs.Count(item => item.Paid), valueStyle: 45);
        return;

        void Section(int index, string label)
        {
            Row(writer, index, 23, [label], [3]);
            _summaryMerges.Add(new MergeCell { Reference = $"A{index}:G{index}" });
        }

        void Metric(int index, string label, object value, bool total = false, uint valueStyle = 33, decimal? rate = null)
        {
            Row(writer, index, total ? 27 : 23, [label, null, null, null, null, rate, value],
                total ? [10, 42, 42, 42, 42, 42, 39] : [index % 2 == 0 ? 40U : 41U, 6, 6, 6, 6, 44, valueStyle]);
            var lastColumn = index == 12 ? "E" : "F";
            _summaryMerges.Add(new MergeCell { Reference = $"A{index}:{lastColumn}{index}" });
        }
    }

    private void Credit(OpenXmlWriter writer)
    {
        Header(writer, ["Datum a čas", "Uživatel", "E-mail", "Druh pohybu", "Změna (Kč)", "Zůstatek (Kč)",
            "Popis operace", "ID operace"], 4);
        for (var i = 0; i < report.Movements.Length; i++)
        {
            var item = report.Movements[i];
            Row(writer, i + 6, 21, [item.At, item.Party.Name, item.Party.Email, MovementLabel(item.Kind),
                Money(item.SignedAmount), Money(item.Balance), item.Description, item.Id.ToString("D")],
                i % 2 == 0 ? [31, 5, 5, 5, 33, 33, 5, 5] : [35, 8, 8, 8, 37, 37, 8, 8]);
        }

        var last = LastData(report.Movements.Length);
        Row(writer, last + 1, 27, ["ZMĚNA KREDITU CELKEM", null, null, null, Sum("E", last, report.MovementTotal)],
            [22, 22, 22, 22, 39]);
    }

    private void Balances(OpenXmlWriter writer)
    {
        Header(writer, ["Uživatel", "E-mail", "Poslední pohyb", "Zůstatek (Kč)", "Poslední operace"], 4);
        for (var i = 0; i < report.Balances.Length; i++)
        {
            var item = report.Balances[i];
            Row(writer, i + 6, 21, [item.Party.Name, item.Party.Email, item.LastAt, Money(item.Closing), item.LastDescription],
                i % 2 == 0 ? [5, 5, 31, 33, 5] : [8, 8, 35, 37, 8]);
        }

        var last = LastData(report.Balances.Length);
        Row(writer, last + 1, 27, ["ZŮSTATKY CELKEM", null, null,
            report.LedgerComplete ? Sum("D", last, report.Closing) : "Neúplná historie"], [22, 22, 24, 39]);
    }

    private void Jobs(OpenXmlWriter writer)
    {
        Header(writer, ["Číslo zakázky", "Pracoviště", "Zákazník", "Název", "Cena (Kč)", "Stav úhrady"], 4);
        for (var i = 0; i < report.Jobs.Length; i++)
        {
            var item = report.Jobs[i];
            Row(writer, i + 6, 21, [item.Job.Number, item.Unit?.DisplayName, item.Party.Name, item.Job.Title,
                Money(item.Job.PriceMinorUnits), item.PaymentStatus],
                i % 2 == 0 ? [5, 5, 5, 5, 33, 5] : [8, 8, 8, 8, 37, 8]);
        }
    }

    private void Controls(OpenXmlWriter writer)
    {
        Header(writer, ["Kontrola", "Hodnota", "Závěr / vymezení", "Zdroj"], 4);
        Control(6, "Karetní platby v exportu", report.Card.Count(item => item.Received != 0), "Dokončené doložené příjmy.", "Payment");
        Control(7, "Úspěšné dobití / kredit", report.Card.Count(item => item.Wallet && item.Received != 0), "Platba s doloženým pohybem.", "Payment + CreditMovement");
        Control(8, "Přímá platba zakázky", report.Card.Count(item => !item.Wallet && item.Received != 0), "Platba s doloženým settlementem.", "Payment + Job");
        Control(9, "Neúspěšný nebo otevřený pokus", report.UnsuccessfulAttempts, "Kohorta založených pokusů bez dokončení v tomto výkazu.", "Payment.CreatedAt");
        Control(10, "Vrácení / kreditní pohyb", report.Card.Count(item => item.Wallet && item.Returned != 0), "Vratky dobití s ověřeným debetem.", "SettlementReturn + CreditMovement");
        Control(11, "Doklady uvedené v ČSOB", report.Card.Select(item => item.DocumentId).Distinct().Count(), "Počet různých dohledatelných dokladů.", "FinancialDocument");
        Control(12, "Kreditní pohyby za období", report.Movements.Length, "Počet podkladových pohybů.", "CreditMovement.RecordedAt");
        Control(13, "Návaznost kreditních zůstatků", report.LedgerComplete ? "OK" : "Neúplné", "Řada, running balance a uložený stav účtu.", "CreditMovement + CreditAccount");
        Control(14, "Převody SafeQ za období", Money(report.MovementValue(AccountingMovementKind.SafeQ)), "Převzatý kredit.", "LegacySafeQCreditTransfer");
        Control(15, "Tisk hrazený kreditem", -Money(report.MovementValue(AccountingMovementKind.Print)), "Pouze potvrzené debety.", "PrintReservation + CreditMovement");
        Control(16, "Dobití kartou", Money(report.Card.Where(item => item.Wallet).Sum(item => item.Received)), "Doložené karetní příjmy do kreditu.", "Payment");
        Control(17, "Přímá platba kartou", Money(report.Card.Where(item => !item.Wallet).Sum(item => item.Received)), "Doložené úhrady kartou.", "Payment");
        Control(18, "Vrácení kartou", Money(report.Returned), "Doložené dokončené vratky obou typů.", "SettlementReturn");
        Control(19, "Čistý karetní objem", Money(report.NetCard), "Evidované příjmy po vratkách.", "Payment + SettlementReturn");
        Control(20, "Převzatý kredit k hranici", report.LedgerComplete ? Money(report.Opening) : "Neověřeno", "Stav před začátkem období.", "CreditMovement");
        Control(21, "Změna kreditu v období", Money(report.MovementTotal), "Podepsané pohyby v období.", "CreditMovement");
        Control(22, "Zůstatky k hranici období", report.LedgerComplete ? Money(report.Closing) : "Neověřeno", "Stav před okamžikem řezu.", "CreditMovement");
        Control(23, "Kreditní identita", report.LedgerComplete && checked(report.Opening + report.MovementTotal) == report.Closing ? "OK" : "Neúplné",
            "Otevření + změny = konec.", "CreditMovement + CreditAccount");
        Control(24, "Účty jen s přenosem", report.CarriedAccounts, "Nenulový počátek bez pohybu v období.", "CreditMovement");
        Control(25, "Rozsah výpisu kreditů", report.LedgerComplete ? "Souvislá známá historie" : "Neúplná historie", "Nezávislý historický podklad může být potřeba.", "CreditAccount");
        Control(26, "Poplatky ČSOB", report.FeeModelAvailable ? "Interní model" : "N/A",
            "99 Kč a 1 % kladného čistého objemu v uzavřeném měsíci s úplnými karetními podklady.", "Přehled");
        Control(27, "Doklad / DPH", report.VerifiedDocuments, "Počet ověřených úplných snapshotů v rozsahu.", "FinancialDocument");
        Control(28, "Bankovní vypořádání", "Neověřeno", "Vyžaduje nezávislý výpis ČSOB.", "Externí podklad");
        Control(29, "Kontrola dokladů k načteným zdrojům", report.Problems.Count(item => item.Code.StartsWith("DOCUMENT", StringComparison.Ordinal)),
            "Počet diagnostik. Doklady mimo načtené zdroje se nekontrolují.", "FinancialDocument");
        Row(writer, 32, 23, ["ZDROJOVÉ EXPORTY"], [5]);
        _controlMerges.Add(new MergeCell { Reference = "A32:D32" });
        Control(33, "CSOB", "Vazby a pokusy", "Výkaz vychází z jednotlivých událostí.", "PaymentInitiation / Attempt");
        Control(34, "PAYMENTS", "Dokončení", "Evidence lokálního dokončení.", "Payment / SettlementReturn");
        Control(35, "CREDIT", "Ledger", "Účetní zůstatky podle časového řezu.", "CreditMovement");
        Control(36, "JOBS", "Vznik a úhrada", "Kohorta zakázek a zvlášť jejich rozúčtování.", "Job / ServiceUnit");

        Row(writer, 39, 28, ["Vazba události", "OrderNo / typ", "Platba a doklad", "Zdroj"], [16, 16, 16, 16]);
        for (var i = 0; i < report.Card.Length; i++)
        {
            var item = report.Card[i];
            Control(40 + i, item.Id.ToString("D"), $"{item.OrderNumber} / {item.Operation}",
                $"Platba {item.PaymentId:D} | Doklad {item.DocumentId:D}", item.Returned == 0 ? "Payment" : "SettlementReturn");
        }

        var line = report.Card.Length + 41;
        Control(line++, "Zachycený snapshot UTC", $"{report.CapturedAt:O}", "Čas snímku reportu.", "Databázový snapshot");
        Control(line++, "Otevřené vratky", report.OpenReturns, "Samostatná provozní informace.", "SettlementReturn.RequestedAt");
        Control(line++, "Účinky na hranici období", report.CrossBoundaryEffects, "Událost nebo pohyb je v jiném období.", "Payment a ledger/Job");
        Control(line++, "Ruční dobití mimo ČSOB", Money(report.MovementValue(AccountingMovementKind.ManualTopUp)), "Evidovaný ruční příjem.", "ManualCreditTopUp");
        Control(line++, "Kreditní vratky zakázek", Money(report.MovementValue(AccountingMovementKind.JobReturn)), "Kompenzace do kreditu.", "SettlementReturn");
        Control(line++, "Kreditní korekce", Money(report.MovementValue(AccountingMovementKind.Adjustment)), "Administrativní korekce.", "CreditAdjustment");
        Control(line++, "Pohyby bez ověřeného původu", Money(report.MovementValue(AccountingMovementKind.Unknown)), "Zůstávají součástí ledgeru.", "CreditMovement");
        Control(line++, "Lokální příjmové kandidáty", Money(report.RawTotals.Receipts),
            $"{report.RawTotals.Payments} dokončených plateb před kontrolou vazeb.", "Samostatná agregace Payment");
        Control(line++, "Lokální kandidáty vratek", Money(report.RawTotals.ReturnAmounts),
            $"{report.RawTotals.Returns} dokončených vratek před kontrolou vazeb.", "Samostatná agregace SettlementReturn");
        Control(line++, "Rozdíl příjmů po kontrole vazeb", Money(checked(report.RawTotals.Receipts - report.Received)),
            "Částka mimo doložený součet kvůli chybějící nebo konfliktní evidenci.", "Payment");
        Control(line++, "Rozdíl vratek po kontrole vazeb", Money(checked(report.RawTotals.ReturnAmounts - report.Returned)),
            "Částka mimo doložený součet kvůli chybějící nebo konfliktní evidenci.", "SettlementReturn");
        foreach (var issue in report.Problems)
        {
            var reference = issue.Id.HasValue ? $" ({issue.Id:D})" : string.Empty;
            Control(line++, issue.Code, issue.Amount.HasValue ? Money(issue.Amount.Value) : "Zkontrolovat",
                issue.Reason + reference, issue.Source);
        }
        return;

        void Control(int index, string label, object value, string note, string source)
        {
            var stripe = index % 2 == 0;
            uint normal = stripe ? 5U : 8U;
            uint metric = value is decimal ? stripe ? 33U : 37U : stripe ? 26U : 27U;
            Row(writer, index, 21, [label, value, note, source], [normal, metric, normal, normal]);
        }
    }

    private void Centres(OpenXmlWriter writer)
    {
        var incomplete = report.CentreAmountsIncomplete;
        var notices = new List<string>();
        if (incomplete)
        {
            notices.Add("NEÚPLNÉ FINANČNÍ PODKLADY. Součet zahrnuje jen doložené částky.");
        }
        if (report.Problems.Any(item => item.Code == "PRINT_SOURCE"))
        {
            notices.Add("Tisk bez přiřazení. Doložená částka zůstává v součtu.");
        }
        if (report.Problems.Any(item => item.Code == "JOB_UNIT"))
        {
            notices.Add("Zakázky bez přiřazení. Doložená částka zůstává v součtu.");
        }
        if (notices.Count > 0)
        {
            Row(writer, 4, 23, [string.Join(" ", notices) + " Viz Kontroly."], [10]);
            _centreMerges.Add(new MergeCell { Reference = "A4:I4" });
        }

        Header(writer, ["Středisko", "Kód pracoviště", "Zakázky kartou", "Zakázky kreditem", "Tisk kreditem",
            "Vratky služby", "Čisté čerpání", "Počet / operace", "Zdroj"], 2);
        var index = 6;
        var groups = report.Centres.ToLookup(item => item.Key);
        var buckets = report.Units.Select(unit => (Key: $"job:{unit.Id}", Code: (string?)unit.Code, Name: unit.DisplayName))
            .Concat(groups.Select(group =>
            {
                var item = group.First();
                return (item.Key, Code: item.UnitCode, item.Name);
            }))
            .DistinctBy(item => item.Key)
            .OrderBy(item => item.Code ?? "~", StringComparer.Ordinal).ThenBy(item => item.Key, StringComparer.Ordinal);
        foreach (var item in buckets)
        {
            var group = groups[item.Key];
            var card = group.Sum(row => row.Card);
            var credit = group.Sum(row => row.Credit);
            var print = group.Sum(row => row.Print);
            var returned = group.Sum(row => row.Returned);
            Row(writer, index++, 21, [item.Name, item.Code, Money(card), Money(credit), Money(print),
                Money(returned), Money(checked(card + credit + print - returned)), group.Count()],
                [5, 5, 33, 33, 33, 33, 34, 45]);
        }

        Row(writer, index++, 27, [incomplete ? "DOLOŽENÁ ČÁST" : "CELKEM", null, Money(report.Centres.Sum(item => item.Card)),
            Money(report.Centres.Sum(item => item.Credit)), Money(report.Centres.Sum(item => item.Print)),
            Money(report.Centres.Sum(item => item.Returned)),
            Money(report.Centres.Sum(item => checked(item.Card + item.Credit + item.Print - item.Returned))),
            report.Centres.Length], [22, 22, 39, 39, 39, 39, 39, 45]);
        index++;
        Row(writer, index++, 28,
            ["Detail událostí", "Datum a čas", "Zakázky kartou", "Zakázky kreditem", "Tisk kreditem", "Vratka",
                "Čistě", "ID události / typ", "ID zakázky / zdroje tisku"],
            [16, 16, 17, 17, 17, 17, 17, 16, 16]);
        foreach (var item in report.Centres)
        {
            Row(writer, index++, 21, [item.Name, item.At, Money(item.Card), Money(item.Credit), Money(item.Print),
                Money(item.Returned), Money(checked(item.Card + item.Credit + item.Print - item.Returned)),
                $"{item.Id:D} / {item.Kind}", item.SourceId.ToString("D")],
                [5, 31, 33, 33, 33, 33, 34, 5, 5]);
        }
    }

    private void Header(OpenXmlWriter writer, string[] labels, int textColumns) =>
        Row(writer, 5, 28, labels, labels.Select((_, index) => index < textColumns ? 16U : 17U).ToArray());

    private static Formula Sum(string column, int last, long total) => new($"SUM({column}6:{column}{last})", Money(total));

    private static string MovementLabel(AccountingMovementKind kind) => kind switch
    {
        AccountingMovementKind.CardTopUp => "Dobití kartou",
        AccountingMovementKind.CardTopUpReturn => "Storno karty",
        AccountingMovementKind.SafeQ => "Převod SafeQ",
        AccountingMovementKind.Job => "Úhrada zakázky",
        AccountingMovementKind.JobReturn => "Vratka zakázky",
        AccountingMovementKind.Print => "Tisk",
        AccountingMovementKind.ManualTopUp => "Ruční dobití",
        AccountingMovementKind.Adjustment => "Korekce kreditu",
        _ => "Neurčený původ"
    };
}
