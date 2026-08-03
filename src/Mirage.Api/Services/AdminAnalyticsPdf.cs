using Mirage.Api.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Mirage.Api.Services;

internal static class AdminAnalyticsPdf
{
    private const string Purple = "#6D4AFF";
    private const string Ink = "#18151F";

    public static byte[] Generate(AdminComprehensiveAnalyticsResponse report)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(x => x.FontSize(9).FontColor(Ink));
            page.Header().Column(header =>
            {
                header.Item().Text("MIRAGE • CONFIDENTIAL").FontColor(Purple).Bold().FontSize(9);
                header.Item().Text("Platform analytics report").Bold().FontSize(22);
                header.Item().Text($"{report.From:dd MMM yyyy} – {report.To:dd MMM yyyy} | Geography: {report.Country ?? "All countries"}")
                    .FontColor(Colors.Grey.Darken1);
                header.Item().PaddingTop(8).LineHorizontal(1).LineColor(Purple);
            });
            page.Content().PaddingVertical(16).Column(column =>
            {
                column.Spacing(14);
                column.Item().Text("Executive overview").Bold().FontSize(15);
                column.Item().Row(row =>
                {
                    Metric(row, "Registered", report.Users.RegisteredUsers.ToString("N0"));
                    Metric(row, "Active (30 days)", report.Users.ActiveUsers.ToString("N0"));
                    Metric(row, "Inactive", report.Users.InactiveUsers.ToString("N0"));
                    Metric(row, "New registrations", report.NewRegistrations.ToString("N0"));
                });
                column.Item().Text("A user is active when the account is enabled and its last successful authentication occurred within 30 days of report generation. Inactive includes dormant, never-authenticated, and suspended accounts.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);

                Section(column, "Account health", table =>
                {
                    Row(table, "Enabled accounts", report.Users.EnabledUsers);
                    Row(table, "Suspended accounts", report.Users.SuspendedUsers);
                    Row(table, "Never logged in", report.Users.NeverLoggedInUsers);
                    Row(table, "Dormancy cutoff (UTC)", report.Users.InactivityCutoff.ToString("dd MMM yyyy HH:mm"));
                });

                Section(column, "Account tiers", table =>
                {
                    TableHeader(table, "Tier", "Users", "Active", "Inactive");
                    foreach (var tier in report.Tiers)
                        TableRow(table, tier.Tier.ToString(), tier.Users.ToString("N0"),
                            tier.ActiveUsers.ToString("N0"), tier.InactiveUsers.ToString("N0"));
                });

                Section(column, "Revenue by source and currency", table =>
                {
                    TableHeader(table, "Source / currency", "Gross", "Mirage revenue", "Provider payable");
                    foreach (var revenue in report.Revenue)
                        TableRow(table, $"{revenue.Source} / {revenue.Currency}", Money(revenue.GrossAmount, revenue.Currency),
                            Money(revenue.PlatformRevenue, revenue.Currency), Money(revenue.ProviderPayable, revenue.Currency));
                    if (report.Revenue.Count == 0) TableRow(table, "No successful transactions", "—", "—", "—");
                });
                column.Item().Text("Revenue recognition note: Mirage revenue is the commission snapshotted on successful counselling payments. Gross charges and counsellor amounts are shown separately and currencies are never combined. Other income sources are excluded until supported by an auditable transaction ledger.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);

                Section(column, "Operational indicators", table =>
                {
                    Row(table, "Completed counselling sessions (period)", report.CompletedCounsellingSessions);
                    Row(table, "Approved couples", report.ApprovedCouples);
                    Row(table, "Approved organisations", report.ApprovedOrganisations);
                    Row(table, "Approved counsellors", report.ApprovedCounsellors);
                    Row(table, "Approved mentors", report.ApprovedMentors);
                    Row(table, "Open content reports", report.OpenContentReports);
                });

                Section(column, "Geographic distribution", table =>
                {
                    TableHeader(table, "Country", "Users", "Active", "Period registrations");
                    foreach (var country in report.Countries.Take(30))
                        TableRow(table, country.Country, country.Users.ToString("N0"), country.ActiveUsers.ToString("N0"),
                            country.RegistrationsInPeriod.ToString("N0"));
                });

                column.Item().Text("Controls and limitations").Bold().FontSize(12);
                column.Item().Text("This report is generated from Mirage's operational database at a point in time. Payment figures include successful transactions only and should be reconciled with payment-provider settlement statements and the general ledger before statutory audit use. Country is based on the user's current profile and may be self-reported. All timestamps and range boundaries are UTC.");
            });
            page.Footer().DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1)).AlignCenter().Text(text =>
            {
                text.Span($"Generated {report.GeneratedAt:dd MMM yyyy HH:mm} UTC • Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        })).GeneratePdf();
    }

    private static void Metric(RowDescriptor row, string label, string value) => row.RelativeItem().PaddingRight(6)
        .Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
        {
            c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
            c.Item().Text(value).Bold().FontSize(15);
        });

    private static void Section(ColumnDescriptor column, string title, Action<TableDescriptor> content)
    {
        column.Item().Text(title).Bold().FontSize(12);
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });
            content(table);
        });
    }

    private static void TableHeader(TableDescriptor table, params string[] values)
    {
        foreach (var value in values) table.Cell().Background(Purple).Padding(5).Text(value).FontColor(Colors.White).Bold();
    }

    private static void TableRow(TableDescriptor table, params string[] values)
    {
        foreach (var value in values) table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(value);
    }

    private static void Row(TableDescriptor table, string label, object value) =>
        TableRow(table, label, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "", "", "");

    private static string Money(decimal amount, string currency) => $"{currency} {amount:N2}";
}
