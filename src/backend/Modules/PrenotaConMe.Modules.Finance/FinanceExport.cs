using System.Globalization;
using ClosedXML.Excel;
using PrenotaConMe.Modules.Finance.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PrenotaConMe.Modules.Finance;

/// <summary>What the PDF and the Excel file show: the report plus names and labels in the business language.</summary>
internal sealed record FinanceDocument(
    FinanceReportDto Report, string BusinessName, string Language, string? LocationName, IReadOnlyDictionary<Guid, string> Locations)
{
    public FinanceTexts Texts { get; } = FinanceTexts.For(Language);

    public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(Language switch { "es" => "es-ES", "en" => "en-IE", _ => "it-IT" });

    public string Title => $"{BusinessName} · {MonthName}" + (LocationName is null ? string.Empty : $" · {LocationName}");

    public string MonthName
    {
        get
        {
            var month = DateOnly.ParseExact(Report.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var name = month.ToString("MMMM yyyy", Culture);
            return char.ToUpper(name[0], Culture) + name[1..];
        }
    }

    public string Money(decimal value) => value.ToString("C", Culture);

    public string LocationOf(Guid? id) => id is { } l ? Locations.GetValueOrDefault(l) ?? "?" : Texts.AllLocations;
}

internal sealed record FinanceTexts(
    string Summary, string Income, string FixedExpenses, string VariableExpenses, string Net, string Appointments, string NoShows,
    string PreviousMonth, string Expected, string Staff, string Name, string Sales, string Services, string Service, string Count,
    string Expenses, string Kind, string Fixed, string Variable, string Amount, string Location, string AllLocations, string Date,
    string Unmarked)
{
    public static FinanceTexts For(string language) => language switch
    {
        "es" => new("Resumen", "Ingresos", "Gastos fijos", "Gastos variables", "Beneficio neto", "Citas", "No presentados",
            "Mes anterior", "Previsto (citas pendientes)", "Colaboradores", "Nombre", "Ventas", "Servicios", "Servicio", "Cantidad",
            "Gastos", "Tipo", "Fijo", "Variable", "Importe", "Sede", "Todas las sedes", "Fecha",
            "Citas pasadas sin marcar (contadas a precio de lista)"),
        "en" => new("Summary", "Income", "Fixed expenses", "Variable expenses", "Net profit", "Appointments", "No-shows",
            "Previous month", "Expected (upcoming appointments)", "Staff", "Name", "Sales", "Services", "Service", "Count",
            "Expenses", "Type", "Fixed", "Variable", "Amount", "Location", "All locations", "Date",
            "Past appointments not marked (counted at list price)"),
        _ => new("Riepilogo", "Incassi", "Spese fisse", "Spese variabili", "Utile netto", "Appuntamenti", "Non presentati",
            "Mese precedente", "Previsto (appuntamenti in arrivo)", "Collaboratori", "Nome", "Vendite", "Servizi", "Servizio", "Quantità",
            "Spese", "Tipo", "Fissa", "Variabile", "Importo", "Sede", "Tutte le sedi", "Data",
            "Appuntamenti passati non segnati (contati a prezzo di listino)"),
    };
}

/// <summary>F4-06: the monthly report as PDF (QuestPDF, Community license) and Excel (ClosedXML).</summary>
internal static class FinanceExport
{
    public static byte[] Pdf(FinanceDocument d)
    {
        var r = d.Report;
        var t = d.Texts;
        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(10));
            page.Header().Text(d.Title).FontSize(16).Bold();
            page.Content().PaddingVertical(12).Column(col =>
            {
                col.Spacing(14);
                col.Item().Element(c => Table(c, [
                    [t.Income, d.Money(r.Totals.Income), d.Money(r.Previous.Income)],
                    [t.FixedExpenses, d.Money(r.Totals.FixedExpenses), d.Money(r.Previous.FixedExpenses)],
                    [t.VariableExpenses, d.Money(r.Totals.VariableExpenses), d.Money(r.Previous.VariableExpenses)],
                    [t.Net, d.Money(r.Totals.Net), d.Money(r.Previous.Net)],
                    [t.Appointments, Int(r.Totals.Appointments), Int(r.Previous.Appointments)],
                    [t.NoShows, Int(r.Totals.NoShows), Int(r.Previous.NoShows)],
                ], header: [t.Summary, d.MonthName, t.PreviousMonth]));
                if (r.ExpectedIncome > 0)
                {
                    col.Item().Text($"{t.Expected}: {d.Money(r.ExpectedIncome)}");
                }

                if (r.Unmarked > 0)
                {
                    col.Item().Text($"{t.Unmarked}: {r.Unmarked}").FontColor(Colors.Grey.Darken1);
                }

                col.Item().Text(t.Staff).FontSize(13).Bold();
                col.Item().Element(c => Table(c, r.Staff.Select(s => new[]
                    { s.Name, Int(s.Appointments), Int(s.NoShows), d.Money(s.Sales) }).ToList(),
                    header: [t.Name, t.Appointments, t.NoShows, t.Sales]));
                foreach (var s in r.Staff.Where(s => s.Services.Count > 0))
                {
                    col.Item().PaddingLeft(12).Element(c => Table(c, s.Services.Select(x => new[]
                        { x.ServiceName, Int(x.Count), d.Money(x.Sales) }).ToList(), header: [$"{s.Name} · {t.Service}", t.Count, t.Sales]));
                }

                col.Item().Text(t.Services).FontSize(13).Bold();
                col.Item().Element(c => Table(c, r.Services.Select(x => new[]
                    { x.ServiceName, Int(x.Count), d.Money(x.Sales) }).ToList(), header: [t.Service, t.Count, t.Sales]));

                col.Item().Text(t.Expenses).FontSize(13).Bold();
                col.Item().Element(c => Table(c, r.Expenses.Select(e => new[]
                    { e.Name, e.Kind == ExpenseKind.Fixed ? t.Fixed : t.Variable, d.LocationOf(e.LocationId), d.Money(e.Amount) }).ToList(),
                    header: [t.Name, t.Kind, t.Location, t.Amount]));
            });
            page.Footer().AlignRight().Text(x =>
            {
                x.CurrentPageNumber();
                x.Span(" / ");
                x.TotalPages();
            });
        })).GeneratePdf();
    }

    public static byte[] Excel(FinanceDocument d)
    {
        var r = d.Report;
        var t = d.Texts;
        using var workbook = new XLWorkbook();

        var summary = workbook.AddWorksheet(t.Summary);
        summary.Cell(1, 1).Value = d.Title;
        summary.Cell(1, 1).Style.Font.Bold = true;
        Rows(summary, 3, [t.Summary, d.MonthName, t.PreviousMonth], [
            [t.Income, r.Totals.Income, r.Previous.Income],
            [t.FixedExpenses, r.Totals.FixedExpenses, r.Previous.FixedExpenses],
            [t.VariableExpenses, r.Totals.VariableExpenses, r.Previous.VariableExpenses],
            [t.Net, r.Totals.Net, r.Previous.Net],
            [t.Appointments, r.Totals.Appointments, r.Previous.Appointments],
            [t.NoShows, r.Totals.NoShows, r.Previous.NoShows],
            [t.Expected, r.ExpectedIncome, null],
            [t.Unmarked, r.Unmarked, null],
        ], moneyRows: [0, 1, 2, 3, 6]);

        var staff = workbook.AddWorksheet(t.Staff);
        var staffRows = r.Staff.SelectMany(s => s.Services.Select(x => new object?[] { s.Name, x.ServiceName, x.Count, x.Sales })
            .Prepend([s.Name, null, s.Appointments, s.Sales])).ToList();
        Rows(staff, 1, [t.Name, t.Service, t.Appointments, t.Sales], staffRows, moneyColumn: 4);

        var services = workbook.AddWorksheet(t.Services);
        Rows(services, 1, [t.Service, t.Count, t.Sales], r.Services.Select(x => new object?[] { x.ServiceName, x.Count, x.Sales }).ToList(), moneyColumn: 3);

        var expenses = workbook.AddWorksheet(t.Expenses);
        Rows(expenses, 1, [t.Name, t.Kind, t.Location, t.Date, t.Amount], r.Expenses.Select(e => new object?[]
            { e.Name, e.Kind == ExpenseKind.Fixed ? t.Fixed : t.Variable, d.LocationOf(e.LocationId), e.Date.ToDateTime(TimeOnly.MinValue), e.Amount }).ToList(),
            moneyColumn: 5, dateColumn: 4);

        foreach (var sheet in workbook.Worksheets)
        {
            sheet.Columns().AdjustToContents();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Table(IContainer container, IReadOnlyList<string[]> rows, string[] header)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                for (var i = 1; i < header.Length; i++)
                {
                    c.RelativeColumn(header.Length > 3 && i < header.Length - 1 ? 1.5f : 2);
                }
            });
            table.Header(h =>
            {
                for (var i = 0; i < header.Length; i++)
                {
                    var cell = h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingVertical(4);
                    (i == 0 ? cell : cell.AlignRight()).Text(header[i]).Bold();
                }
            });
            foreach (var row in rows)
            {
                for (var i = 0; i < row.Length; i++)
                {
                    var cell = table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3);
                    (i == 0 ? cell : cell.AlignRight()).Text(row[i]);
                }
            }
        });
    }

    private static void Rows(
        IXLWorksheet sheet, int startRow, string[] header, List<object?[]> rows, int[]? moneyRows = null, int? moneyColumn = null,
        int? dateColumn = null)
    {
        for (var c = 0; c < header.Length; c++)
        {
            sheet.Cell(startRow, c + 1).Value = header[c];
            sheet.Cell(startRow, c + 1).Style.Font.Bold = true;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = sheet.Cell(startRow + 1 + r, c + 1);
                cell.Value = rows[r][c] switch
                {
                    null => Blank.Value,
                    string s => s,
                    int i => i,
                    decimal m => m,
                    DateTime dt => dt,
                    var other => other.ToString(),
                };
                if ((moneyRows?.Contains(r) == true && c > 0) || moneyColumn == c + 1)
                {
                    cell.Style.NumberFormat.Format = "#,##0.00 €";
                }

                if (dateColumn == c + 1)
                {
                    cell.Style.DateFormat.Format = "dd/mm/yyyy";
                }
            }
        }
    }
}
