using System.Text;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ShanthiNikethan.EmployeeManagement.Modules.Payroll.Data;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;

namespace ShanthiNikethan.EmployeeManagement.Modules.Payroll.Services;

public interface IPayrollExportService
{
    /// <summary>
    /// Generates the bank-upload CSV for one designation category. Only
    /// includes staff on IOB bulk upload — anyone paid by manual NEFT
    /// (e.g. a non-IOB account) is deliberately excluded, since this file
    /// goes straight into the bank's own upload portal.
    /// </summary>
    byte[] GenerateCsv(PayrollRun run, List<PayrollLineItem> lineItems, StaffDesignation designation);

    /// <summary>
    /// Generates the full wage statement PDF — one page per designation
    /// category, every active staff member regardless of bank mode (since
    /// this is the complete picture for regulatory filing, not a bank
    /// upload file).
    /// </summary>
    byte[] GeneratePdf(PayrollRun run, List<PayrollLineItem> lineItems);
}

public class PayrollExportService : IPayrollExportService
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public PayrollExportService(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    public byte[] GenerateCsv(PayrollRun run, List<PayrollLineItem> lineItems, StaffDesignation designation)
    {
        var monthName = new DateOnly(run.Year, run.Month, 1).ToString("MMMM");
        var narrationLabel = run.RunTypeLabel.Replace(" ", "");
        var rows = lineItems
            .Where(li => li.Designation == designation && li.BankMode == BankPaymentMode.IobBulkUpload)
            .OrderBy(li => li.DisplayOrder)
            .ToList();

        var sb = new StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            var li = rows[i];
            var amount = (long)Math.Round(li.NetPay, 0, MidpointRounding.AwayFromZero);
            var nameNoSpaces = li.DisplayName.Replace(" ", "");
            sb.Append($"APW,{amount},{li.BankAccountNumber},{li.DisplayName},INR,{nameNoSpaces}{narrationLabel}{monthName}{run.Year};");
            if (i < rows.Count - 1) sb.Append("\r\n");
        }

        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    public byte[] GeneratePdf(PayrollRun run, List<PayrollLineItem> lineItems)
    {
        var logoPath = Path.Combine(_env.WebRootPath, "img", "logo-emblem-black.png");
        byte[]? logoBytes = File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;

        var teaching = lineItems.Where(li => li.Designation == StaffDesignation.Teaching).OrderBy(li => li.DisplayOrder).ToList();
        var nonTeaching = lineItems.Where(li => li.Designation == StaffDesignation.NonTeaching).OrderBy(li => li.DisplayOrder).ToList();

        var document = Document.Create(container =>
        {
            if (teaching.Count > 0)
                ComposeLetter(container, "Teaching Staff", teaching, run, logoBytes);
            if (nonTeaching.Count > 0)
                ComposeLetter(container, "Non-Teaching Staff", nonTeaching, run, logoBytes);
        });

        return document.GeneratePdf();
    }

    /// <summary>
    /// Composes one section as a formal letter addressed to the bank
    /// requesting the EFT credit — not a payroll register. That's a
    /// deliberate distinction: this document is what gets handed/emailed
    /// to the bank branch to authorize the transfer, styled accordingly
    /// (letterhead, salutation, subject line, signature block), rather
    /// than an internal data table.
    /// </summary>
    private static void ComposeLetter(IDocumentContainer container, string staffCategoryLabel, List<PayrollLineItem> items,
        PayrollRun run, byte[]? logoBytes)
    {
        var total = items.Sum(li => li.NetPay);
        var monthLabel = run.MonthLabel;
        var sectionTitle = staffCategoryLabel.ToUpperInvariant().Replace(" STAFF", "");
        const string fontFamily = "Courier New";

        // The school's traditional exact letterhead wording — matches their
        // historical documents precisely, not derived from the general
        // Institution:Name/Location config values.
        const string letterheadLine = "SHANTHI NIKETHAN MATRIC. HR.SEC.SCHOOL,ARUMBAVUR";
        const string shortName = "SNMHSS";

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontFamily(fontFamily).FontSize(12).FontColor(Colors.Black));

            page.Header().Column(col =>
            {
                col.Item().AlignCenter().Row(row =>
                {
                    if (logoBytes != null)
                        row.AutoItem().Height(50).Image(logoBytes);
                    row.AutoItem().PaddingLeft(14).AlignMiddle().Column(c =>
                    {
                        c.Item().AlignCenter().Text(letterheadLine).Bold().FontSize(13);
                        c.Item().PaddingTop(8).AlignCenter().Text($"{run.RunTypeLabel.ToUpperInvariant()} LIST {sectionTitle} (IOB) - {monthLabel}").Bold().FontSize(12);
                    });
                });
                col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Black);
            });

            page.Content().PaddingTop(14).Column(col =>
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(42);
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(2.2f);
                        columns.RelativeColumn(1.6f);
                    });

                    static IContainer HeaderStyle(IContainer c) =>
                        c.PaddingHorizontal(6).BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(4).DefaultTextStyle(x => x.Bold());

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderStyle).Text("S.No");
                        header.Cell().Element(HeaderStyle).Text("Name");
                        header.Cell().Element(HeaderStyle).Text("Account Number");
                        header.Cell().Element(HeaderStyle).AlignRight().Text("Total Amount");
                    });

                    foreach (var (li, i) in items.Select((li, i) => (li, i)))
                    {
                        static IContainer BodyStyle(IContainer c) =>
                            c.PaddingHorizontal(6).PaddingVertical(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1);

                        table.Cell().Element(BodyStyle).Text((i + 1).ToString());
                        table.Cell().Element(BodyStyle).Text(li.DisplayName);
                        table.Cell().Element(BodyStyle).Text(li.BankAccountNumber);
                        table.Cell().Element(BodyStyle).AlignRight().Text($"Rs. {li.NetPay:N0}");
                    }

                    static IContainer TotalStyle(IContainer c) =>
                        c.PaddingHorizontal(6).BorderTop(1).BorderColor(Colors.Black).PaddingTop(4).DefaultTextStyle(x => x.Bold());

                    table.Cell().ColumnSpan(3).Element(TotalStyle).Text("TOTAL");
                    table.Cell().Element(TotalStyle).AlignRight().Text($"Rs. {total:N0}");
                });

                col.Item().PaddingTop(16).Text(
                    $"Kindly credit {shortName} Staff {run.RunTypeLabel} {new DateOnly(run.Year, run.Month, 1):MMMM}/{run.Year} as per the list given above here-in enclosed with,"
                ).LineHeight(1.3f);

                col.Item().PaddingTop(14).Column(fields =>
                {
                    fields.Item().Row(row =>
                    {
                        row.ConstantItem(140).Text("Check No.").SemiBold();
                        row.RelativeItem().Text("EFT");
                    });
                    fields.Item().PaddingTop(6).Row(row =>
                    {
                        row.ConstantItem(140).Text("Dated").SemiBold();
                        row.RelativeItem().Text($"{DateTime.Now:dd/MM/yyyy}");
                    });
                    fields.Item().PaddingTop(6).Row(row =>
                    {
                        row.ConstantItem(140).Text("Amount").SemiBold();
                        row.RelativeItem().Text($"Rs. {total:N0}");
                    });
                    fields.Item().PaddingTop(6).Row(row =>
                    {
                        row.ConstantItem(140).Text("Amount in Words").SemiBold();
                        row.RelativeItem().Text(IndianNumberToWords.ToRupeesInWords(total));
                    });
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Page ").FontSize(10).FontColor(Colors.Grey.Darken1);
                t.CurrentPageNumber().FontSize(10).FontColor(Colors.Grey.Darken1);
                t.Span(" of ").FontSize(10).FontColor(Colors.Grey.Darken1);
                t.TotalPages().FontSize(10).FontColor(Colors.Grey.Darken1);
            });
        });
    }
}
