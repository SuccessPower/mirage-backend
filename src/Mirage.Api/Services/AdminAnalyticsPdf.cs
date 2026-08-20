using Mirage.Api.Contracts;
using Mirage.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Mirage.Api.Services;

internal static class AdminAnalyticsPdf
{
    private const string Purple = "#6C4EF2";
    private const string PurpleSoft = "#EEEAFE";
    private const string Navy = "#11101A";
    private const string Ink = "#1B1923";
    private const string Muted = "#686474";
    private const string Border = "#E7E4EC";
    private const string Surface = "#F7F6FA";
    private const string Green = "#158F65";
    private const string Amber = "#D97706";
    private const string Rose = "#D1435B";
    private const string LogoSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 236.82 247.98">
          <g fill="#6c4ef2">
            <path d="m236.56,225.9l-16.37-134.06c-1.99-19.29-8.57-26.72-22.53-27.95-13.53-1.19-28.22,11.58-33.28,17.71-12,14.29-30.65,19.76-46.16,13.96-14.31-5.36-20.55-18.29-22.23-22.31-.2-.49-.35-.99-.47-1.5-3.54-15.38-17.37-30.21-32.04-26.64-10.5,2.55-16.76,16.61-22.08,29.77l-20.4,46.7c-10.84,26.55-10.31,25.39-18.65,47.08-3.93,12.66-2.85,22.67,2.76,24.44,18.25,5.74,37.49-60.29,56.66-77.1,16.53-14.49,84.76.24,96.29,12.74,8.08,8.75,14.08,30.4,29.65,65.72,7.89,17.91,22.73,50.04,33.01,52.98,8,2.29,17.84-2.28,15.85-21.54Z"/>
            <circle cx="75.5" cy="16.37" r="16.37"/><circle cx="197.48" cy="32.74" r="16.37"/>
          </g>
        </svg>
        """;

    public static byte[] Generate(AdminComprehensiveAnalyticsResponse report)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(document =>
        {
            document.Page(page => ComposeCover(page, report));
            document.Page(page => ComposeReport(page, report));
        }).GeneratePdf();
    }

    private static void ComposeCover(PageDescriptor page, AdminComprehensiveAnalyticsResponse report)
    {
        page.Size(PageSizes.A4);
        page.Margin(0);
        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.White));
        page.Background().Background(Navy);
        page.Content().Padding(44).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.ConstantItem(42).Padding(2).Svg(LogoSvg);
                row.ConstantItem(12);
                row.RelativeItem().Column(brand =>
                {
                    brand.Item().Text("MIRAGE").Bold().FontSize(20).LetterSpacing(.08f);
                    brand.Item().Text("RELATIONSHIPS • COMMUNITY • GUIDANCE").FontSize(7).FontColor("#AAA5BA");
                });
                row.ConstantItem(130).AlignRight().Text("CONFIDENTIAL\nBOARD REPORT")
                    .Bold().FontSize(7).FontColor("#B8ACFF").LineHeight(1.5f);
            });

            column.Item().PaddingTop(70).Width(72).Height(5).Background(Purple);
            column.Item().PaddingTop(20).Text("Platform\nintelligence report").Bold().FontSize(42)
                .LineHeight(.95f).LetterSpacing(-.04f);
            column.Item().PaddingTop(18).Width(430).Text(
                "A decision-grade view of growth, engagement, monetisation, geographic reach and operational health.")
                .FontSize(14).FontColor("#C8C4D2").LineHeight(1.45f);

            column.Item().PaddingTop(42).Row(row =>
            {
                CoverFact(row, "REPORTING PERIOD", $"{report.From:dd MMM yyyy}\n{report.To:dd MMM yyyy}");
                CoverFact(row, "GEOGRAPHY", report.Country ?? "Global portfolio");
                CoverFact(row, "GENERATED", $"{report.GeneratedAt:dd MMM yyyy}\n{report.GeneratedAt:HH:mm} UTC");
            });

            column.Item().PaddingTop(55).Background("#1A1825").Border(1).BorderColor("#2D293A")
                .Padding(20).Row(row =>
                {
                    CoverMetric(row, report.Users.RegisteredUsers.ToString("N0"), "registered users");
                    CoverMetric(row, Percent(report.Users.ActiveUsers, report.Users.RegisteredUsers), "30-day active rate");
                    CoverMetric(row, report.NewRegistrations.ToString("N0"), "period registrations");
                    CoverMetric(row, report.CompletedCounsellingSessions.ToString("N0"), "completed sessions");
                });

            column.Item().ExtendVertical().AlignBottom().Row(row =>
            {
                row.RelativeItem().Text("Prepared for authorised investors, directors and auditors")
                    .FontSize(8).FontColor("#878292");
                row.AutoItem().Text("MIRAGE • 2026").Bold().FontSize(8).FontColor("#878292");
            });
        });
    }

    private static void ComposeReport(PageDescriptor page, AdminComprehensiveAnalyticsResponse report)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(34);
        page.MarginVertical(28);
        page.DefaultTextStyle(x => x.FontSize(9).FontColor(Ink));
        page.Header().Element(container => Header(container, report));
        page.Footer().Element(container => Footer(container, report));
        page.Content().PaddingVertical(18).Column(column =>
        {
            column.Spacing(14);
            Title(column, "01", "Executive scorecard", "A concise reading of scale, momentum and account quality");
            column.Item().Row(row =>
            {
                Metric(row, "Registered users", report.Users.RegisteredUsers, $"+{report.NewRegistrations:N0} in period", Purple);
                Metric(row, "Active users", report.Users.ActiveUsers, $"{Percent(report.Users.ActiveUsers, report.Users.RegisteredUsers)} of base", Green);
                Metric(row, "Inactive users", report.Users.InactiveUsers, $"{report.Users.NeverLoggedInUsers:N0} never logged in", Amber);
                Metric(row, "Suspended", report.Users.SuspendedUsers, "Trust & safety control", Rose);
            });
            column.Item().Element(c => ExecutiveNarrative(c, report));

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => Panel(c, "Account health", "30-day engagement status", body =>
                {
                    HealthBar(body, "Active", report.Users.ActiveUsers, report.Users.RegisteredUsers, Green);
                    HealthBar(body, "Dormant / inactive", report.Users.InactiveUsers, report.Users.RegisteredUsers, Amber);
                    HealthBar(body, "Suspended", report.Users.SuspendedUsers, report.Users.RegisteredUsers, Rose);
                    body.Item().PaddingTop(6).Text($"Dormancy cutoff: {report.Users.InactivityCutoff:dd MMM yyyy HH:mm} UTC")
                        .FontSize(7).FontColor(Muted);
                }));
                row.ConstantItem(12);
                row.RelativeItem().Element(c => Panel(c, "Tier adoption", "Current account mix", body =>
                {
                    foreach (var tier in report.Tiers)
                        HealthBar(body, tier.Tier.ToString(), tier.Users, report.Users.RegisteredUsers, Purple);
                }));
            });

            column.Item().Element(c => Panel(c, "Gender balance", "Registered members by stated gender", body =>
            {
                foreach (var gender in report.Genders)
                    HealthBar(body, GenderLabel(gender.Sex), gender.Users, report.Users.RegisteredUsers,
                        gender.Sex is null ? Muted : Purple);
                body.Item().Table(table =>
                {
                    Columns(table, 1.6f, 1f, 1f, 1f);
                    TableHeader(table, "Gender", "Members", "Active (30d)", "Registered in period");
                    foreach (var gender in report.Genders)
                        TableRow(table, GenderLabel(gender.Sex), gender.Users.ToString("N0"),
                            gender.ActiveUsers.ToString("N0"), gender.RegistrationsInPeriod.ToString("N0"));
                });
            }));

            column.Item().PageBreak();
            Title(column, "02", "Engagement & conversations", "How members participate across time, gender and region");
            column.Item().Row(row =>
            {
                foreach (var period in report.Engagement.Periods)
                    Metric(row, period.Period, period.Messages,
                        $"{period.Conversations:N0} conversations · {period.EngagedUsers:N0} people", Purple);
            });
            column.Item().Element(c => Panel(c, "Engagement by gender", "Selected reporting period", body =>
            {
                body.Item().Table(table =>
                {
                    Columns(table, 1.6f, 1f, 1f, 1f);
                    TableHeader(table, "Gender", "Engaged people", "Messages", "Platform actions");
                    foreach (var gender in report.Engagement.ByGender)
                        TableRow(table, GenderLabel(gender.Sex), gender.EngagedUsers.ToString("N0"),
                            gender.MessagesSent.ToString("N0"), gender.EngagementEvents.ToString("N0"));
                });
            }));
            column.Item().Element(c => Panel(c, "Chats between genders", "Distinct conversations with message activity", body =>
            {
                var max = Math.Max(1, report.Engagement.ConversationsByGenderPair.DefaultIfEmpty().Max(x => x?.Messages ?? 0));
                foreach (var pair in report.Engagement.ConversationsByGenderPair)
                    HealthBar(body, pair.GenderPair, pair.Messages, max, Purple);
                body.Item().Table(table =>
                {
                    Columns(table, 1.5f, 1f, 1f, 1f);
                    TableHeader(table, "Gender pairing", "Conversations", "Active now", "Messages");
                    foreach (var pair in report.Engagement.ConversationsByGenderPair)
                        TableRow(table, pair.GenderPair, pair.Conversations.ToString("N0"),
                            pair.ActiveConversations.ToString("N0"), pair.Messages.ToString("N0"));
                });
            }));
            column.Item().Element(c => Panel(c, "Regional engagement", "Top regions by engaged people", body =>
            {
                body.Item().Table(table =>
                {
                    Columns(table, 1.7f, .8f, .8f, .8f, .8f);
                    TableHeader(table, "Region", "Users", "Engaged", "Messages", "Actions");
                    foreach (var region in report.Engagement.ByRegion.Take(20))
                        TableRow(table, region.Country, region.Users.ToString("N0"), region.EngagedUsers.ToString("N0"),
                            region.Messages.ToString("N0"), region.EngagementEvents.ToString("N0"));
                });
            }));
            column.Item().Element(c => Panel(c, "Daily activity", "Most recent 21 days in the selected period", body =>
            {
                body.Item().Table(table =>
                {
                    Columns(table, 1.4f, 1f, 1f, 1f);
                    TableHeader(table, "Date", "Messages", "Conversations", "Engaged people");
                    foreach (var day in report.Engagement.DailyTrend.TakeLast(21))
                        TableRow(table, day.Date.ToString("dd MMM yyyy"), day.Messages.ToString("N0"),
                            day.Conversations.ToString("N0"), day.EngagedUsers.ToString("N0"));
                });
            }));

            column.Item().PageBreak();
            Title(column, "03", "Financial performance", "Revenue quality, provider obligations and settlement exposure");
            if (report.Revenue.Count == 0)
            {
                column.Item().Element(c => EmptyPanel(c, "No recognised revenue in this reporting period",
                    "The successful-payment ledger contains no qualifying transactions for the selected date and geography filters."));
            }
            else
            {
                foreach (var revenue in report.Revenue)
                {
                    column.Item().Text(revenue.Currency).Bold().FontSize(11).FontColor(Purple);
                    column.Item().Row(row =>
                    {
                        MoneyMetric(row, "Gross charges", revenue.GrossAmount, revenue.Currency, "Customer payments");
                        MoneyMetric(row, "Mirage revenue", revenue.PlatformRevenue, revenue.Currency, "Recognised commission");
                        MoneyMetric(row, "Provider payable", revenue.ProviderPayable, revenue.Currency, "Counsellor obligation");
                        MoneyMetric(row, "Outstanding", revenue.OutstandingPayout, revenue.Currency, "Unsettled liability");
                    });
                }
            }
            column.Item().Element(c => Panel(c, "Revenue ledger", "Successful counselling charges, separated by currency", body =>
            {
                body.Item().Table(table =>
                {
                    Columns(table, 2.2f, .7f, .7f, 1f, 1f, 1f);
                    TableHeader(table, "Source", "Currency", "Txns", "Gross", "Mirage", "Outstanding");
                    foreach (var revenue in report.Revenue)
                        TableRow(table, revenue.Source, revenue.Currency, revenue.TransactionCount.ToString("N0"),
                            Money(revenue.GrossAmount, revenue.Currency), Money(revenue.PlatformRevenue, revenue.Currency),
                            Money(revenue.OutstandingPayout, revenue.Currency));
                    if (report.Revenue.Count == 0) TableRow(table, "No successful transactions", "—", "—", "—", "—", "—");
                });
            }));
            column.Item().Element(c => Note(c, "REVENUE RECOGNITION",
                "Mirage revenue is the commission snapshotted on successful counselling payments. Gross customer charges and counsellor obligations are presented separately. Currencies are never consolidated without an explicit FX policy. Additional income streams remain excluded until backed by an auditable transaction ledger."));

            column.Item().PageBreak();
            Title(column, "04", "Market footprint", "Geographic reach, concentration and acquisition momentum");
            column.Item().Row(row =>
            {
                var leading = report.Countries.FirstOrDefault();
                Metric(row, "Countries represented", report.Countries.Count, "Current profile geography", Purple);
                Metric(row, "Leading market", leading?.Users ?? 0, leading?.Country ?? "No country data", Green);
                Metric(row, "Period registrations", report.NewRegistrations, "Selected reporting window", Purple);
                Metric(row, "Approved organisations", report.ApprovedOrganisations, "Institutional network", Amber);
            });
            column.Item().Element(c => Panel(c, "Geographic distribution", "Top markets by registered-user base", body =>
            {
                var max = Math.Max(1, report.Countries.Count == 0 ? 1 : report.Countries.Max(x => x.Users));
                foreach (var country in report.Countries.Take(12))
                {
                    body.Item().PaddingVertical(3).Row(row =>
                    {
                        row.ConstantItem(105).Text(string.IsNullOrWhiteSpace(country.Country) ? "Not specified" : country.Country).FontSize(8);
                        row.RelativeItem().PaddingTop(3).Height(8).Background(Border).Layers(layers =>
                        {
                            layers.PrimaryLayer();
                            layers.Layer().Width((float)country.Users / max * 100).Background(Purple);
                        });
                        row.ConstantItem(45).AlignRight().Text(country.Users.ToString("N0")).Bold().FontSize(8);
                    });
                }
                if (report.Countries.Count == 0) body.Item().Text("No geographic data available.").FontColor(Muted);
            }));
            column.Item().Table(table =>
            {
                Columns(table, 2f, 1f, 1f, 1f);
                TableHeader(table, "Country", "Users", "Active", "New in period");
                foreach (var country in report.Countries.Take(30))
                    TableRow(table, country.Country, country.Users.ToString("N0"), country.ActiveUsers.ToString("N0"),
                        country.RegistrationsInPeriod.ToString("N0"));
            });

            column.Item().PageBreak();
            Title(column, "05", "Operations & governance", "Capacity, relationship outcomes and control environment");
            column.Item().Row(row =>
            {
                OperationCard(row, report.CompletedCounsellingSessions, "Completed sessions", "Period throughput", Green);
                OperationCard(row, report.ApprovedCounsellors, "Counsellors", "Approved capacity", Purple);
                OperationCard(row, report.ApprovedMentors, "Mentors", "Approved capacity", Purple);
            });
            column.Item().Row(row =>
            {
                OperationCard(row, report.ApprovedCouples, "Approved couples", "Relationship outcomes", Green);
                OperationCard(row, report.ApprovedOrganisations, "Organisations", "Approved network", Amber);
                OperationCard(row, report.OpenContentReports, "Open reports", "Review exposure", report.OpenContentReports > 0 ? Rose : Green);
            });
            column.Item().Element(c => Panel(c, "Control commentary", "Interpretation for investors and auditors", body =>
            {
                Bullet(body, "Activity", "Active means an enabled account authenticated within 30 days of report generation; inactive includes dormant, never-authenticated and suspended accounts.");
                Bullet(body, "Revenue", "Only successful payment records are recognised. Settlement totals should be reconciled with provider statements and the general ledger.");
                Bullet(body, "Geography", "Country is sourced from each user's current profile and may be self-reported; it is not a verified residency assertion.");
                Bullet(body, "Privacy", "The report contains aggregate operational data and intentionally excludes conversation content and direct personal identifiers.");
                Bullet(body, "Comparability", "Filters apply to period-sensitive measures. Current-state approved inventory is presented as of generation time unless explicitly labelled as period activity.");
            }));
            column.Item().Element(c => Note(c, "AUDIT USE",
                "This document is an operational analytics report, not audited financial statements. It should be paired with payment-provider settlement reports, bank statements, accounting records and control evidence before reliance in a statutory audit or investment transaction."));
            column.Item().PaddingTop(8).Text("End of report").Bold().FontColor(Purple);
        });
    }

    private static void Header(IContainer container, AdminComprehensiveAnalyticsResponse report) => container.Row(row =>
    {
        row.ConstantItem(20).Padding(1).Svg(LogoSvg);
        row.ConstantItem(7);
        row.AutoItem().AlignMiddle().Text("MIRAGE").Bold().FontSize(10).LetterSpacing(.08f);
        row.RelativeItem();
        row.AutoItem().AlignMiddle().Text($"{report.From:dd MMM yyyy} — {report.To:dd MMM yyyy}  •  {report.Country ?? "All countries"}")
            .FontSize(7).FontColor(Muted);
    });

    private static void Footer(IContainer container, AdminComprehensiveAnalyticsResponse report) =>
        container.BorderTop(1).BorderColor(Border).PaddingTop(7).Row(row =>
        {
            row.RelativeItem().Text($"CONFIDENTIAL  •  Generated {report.GeneratedAt:dd MMM yyyy HH:mm} UTC")
                .FontSize(7).FontColor(Muted);
            row.AutoItem().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(7).FontColor(Muted));
                text.Span("MIRAGE INTELLIGENCE  •  ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });

    private static void Title(ColumnDescriptor column, string number, string title, string subtitle)
    {
        column.Item().Row(row =>
        {
            row.ConstantItem(35).Text(number).Bold().FontSize(9).FontColor(Purple);
            row.RelativeItem().Column(c =>
            {
                c.Item().Text(title).Bold().FontSize(21).LetterSpacing(-.02f);
                c.Item().Text(subtitle).FontSize(8).FontColor(Muted);
            });
        });
        column.Item().Height(2).Background(Purple);
    }

    private static void Metric(RowDescriptor row, string label, int value, string detail, string accent)
    {
        row.RelativeItem().PaddingRight(7).Border(1).BorderColor(Border).Background(Colors.White)
            .Padding(11).Column(c =>
            {
                c.Item().Width(22).Height(3).Background(accent);
                c.Item().PaddingTop(8).Text(value.ToString("N0")).Bold().FontSize(20);
                c.Item().Text(label).Bold().FontSize(8);
                c.Item().PaddingTop(3).Text(detail).FontSize(6.8f).FontColor(Muted);
            });
    }

    private static void MoneyMetric(RowDescriptor row, string label, decimal value, string currency, string detail)
    {
        row.RelativeItem().PaddingRight(7).Background(Surface).Padding(11).Column(c =>
        {
            c.Item().Text(label).FontSize(7).FontColor(Muted);
            c.Item().PaddingTop(5).Text(Money(value, currency)).Bold().FontSize(13);
            c.Item().PaddingTop(3).Text(detail).FontSize(6.5f).FontColor(Muted);
        });
    }

    private static void OperationCard(RowDescriptor row, int value, string label, string detail, string accent)
    {
        row.RelativeItem().PaddingRight(9).PaddingBottom(2).BorderLeft(4).BorderColor(accent)
            .Background(Surface).Padding(14).Column(c =>
            {
                c.Item().Text(value.ToString("N0")).Bold().FontSize(22);
                c.Item().Text(label).Bold().FontSize(9);
                c.Item().Text(detail).FontSize(7).FontColor(Muted);
            });
    }

    private static void CoverFact(RowDescriptor row, string label, string value) => row.RelativeItem().Column(c =>
    {
        c.Item().Text(label).Bold().FontSize(7).FontColor("#878292");
        c.Item().PaddingTop(6).Text(value).Bold().FontSize(10).LineHeight(1.35f);
    });

    private static void CoverMetric(RowDescriptor row, string value, string label) => row.RelativeItem().Column(c =>
    {
        c.Item().Text(value).Bold().FontSize(19);
        c.Item().Text(label).FontSize(7).FontColor("#9994A5");
    });

    private static void ExecutiveNarrative(IContainer container, AdminComprehensiveAnalyticsResponse report)
    {
        var activeRate = Percent(report.Users.ActiveUsers, report.Users.RegisteredUsers);
        var leadingMarket = report.Countries.FirstOrDefault()?.Country ?? "no reported market";
        container.Background(PurpleSoft).BorderLeft(4).BorderColor(Purple).Padding(14).Column(c =>
        {
            c.Item().Text("EXECUTIVE READOUT").Bold().FontSize(7).FontColor(Purple);
            c.Item().PaddingTop(5).Text($"Mirage serves {report.Users.RegisteredUsers:N0} registered users with a {activeRate} 30-day active rate. " +
                $"The platform added {report.NewRegistrations:N0} users during the selected period, with {leadingMarket} representing the largest reported market. " +
                $"Operationally, {report.CompletedCounsellingSessions:N0} counselling sessions completed in-period and {report.OpenContentReports:N0} content reports remain open.")
                .FontSize(9).LineHeight(1.45f);
        });
    }

    private static void Panel(IContainer container, string title, string subtitle, Action<ColumnDescriptor> content) =>
        container.Border(1).BorderColor(Border).Padding(14).Column(c =>
        {
            c.Item().Text(title).Bold().FontSize(11);
            c.Item().Text(subtitle).FontSize(7).FontColor(Muted);
            c.Item().PaddingTop(10).Column(content);
        });

    private static void EmptyPanel(IContainer container, string title, string detail) =>
        container.Background(Surface).Padding(20).Column(c =>
        {
            c.Item().Text(title).Bold().FontSize(11);
            c.Item().PaddingTop(5).Text(detail).FontColor(Muted).FontSize(8);
        });

    private static void HealthBar(ColumnDescriptor column, string label, int value, int total, string color)
    {
        var percentage = total == 0 ? 0 : Math.Clamp((float)value / total, 0, 1);
        column.Item().PaddingBottom(7).Column(c =>
        {
            c.Item().Row(row =>
            {
                row.RelativeItem().Text(label).FontSize(7.5f);
                row.AutoItem().Text($"{value:N0}  •  {percentage:P0}").Bold().FontSize(7.5f);
            });
            c.Item().PaddingTop(3).Height(6).Background(Border).Layers(layers =>
            {
                layers.PrimaryLayer();
                layers.Layer().Width(percentage * 100).Background(color);
            });
        });
    }

    private static void Note(IContainer container, string label, string text) =>
        container.Background(Surface).Padding(12).Column(c =>
        {
            c.Item().Text(label).Bold().FontSize(6.5f).FontColor(Purple);
            c.Item().PaddingTop(4).Text(text).FontSize(7.5f).FontColor(Muted).LineHeight(1.35f);
        });

    private static void Bullet(ColumnDescriptor column, string label, string text) => column.Item().PaddingBottom(7).Row(row =>
    {
        row.ConstantItem(9).Text("•").Bold().FontColor(Purple);
        row.RelativeItem().DefaultTextStyle(x => x.FontSize(8).LineHeight(1.35f)).Text(t =>
        {
            t.Span($"{label}: ").Bold();
            t.Span(text);
        });
    });

    private static void Columns(TableDescriptor table, params float[] widths) => table.ColumnsDefinition(columns =>
    {
        foreach (var width in widths) columns.RelativeColumn(width);
    });

    private static void TableHeader(TableDescriptor table, params string[] values)
    {
        foreach (var value in values)
            table.Cell().Background(Navy).PaddingVertical(7).PaddingHorizontal(6).Text(value)
                .FontColor(Colors.White).Bold().FontSize(7);
    }

    private static void TableRow(TableDescriptor table, params string[] values)
    {
        foreach (var value in values)
            table.Cell().BorderBottom(1).BorderColor(Border).PaddingVertical(7).PaddingHorizontal(6).Text(value).FontSize(7.5f);
    }

    private static string GenderLabel(Sex? sex) => sex?.ToString() ?? "Not stated";
    private static string Percent(int part, int whole) => whole == 0 ? "0%" : $"{(decimal)part / whole:P0}";
    private static string Money(decimal amount, string currency) => $"{currency} {amount:N2}";
}
