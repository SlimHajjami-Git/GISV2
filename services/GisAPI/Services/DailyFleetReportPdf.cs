using GisAPI.Application.Features.Reports.Queries.GetDailyActivityReport;
using GisAPI.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GisAPI.Services;

/// <summary>
/// Génère le « Rapport Journalier » de la flotte en PDF, fidèle à l'écran
/// /rapports : pour chaque véhicule -> synthèse (conduite, arrêts, distance,
/// vitesse max, carburant), premier démarrage + adresse, événements carburant,
/// et la chronologie complète des trajets/arrêts (heures, durées, distances,
/// adresses).
///
/// <para><b>Heures</b> : les horodatages de la requête (RecordedAt) sont stockés
/// en UTC alors que la flotte est en Tunisie (UTC+1). On les décale donc de +1h
/// pour afficher l'heure locale, exactement comme l'écran.</para>
/// </summary>
public static class DailyFleetReportPdf
{
    private const string Navy = "#1e3a5f";
    private const string Navy2 = "#2d5a87";
    private const string Ink = "#1e293b";
    private const string Muted = "#64748b";
    private const string Line = "#e2e8f0";
    private const string SoftLine = "#f1f5f9";
    private const string Bg = "#f8fafc";
    private const string Green = "#16a34a";
    private const string Amber = "#d97706";

    private static string Hm(DateTime utc) => utc.AddHours(1).ToString("HH:mm");

    public static byte[] Build(
        Societe societe,
        DateTime reportDate,
        List<DailyActivityReportDto> reports,
        string title = "Rapport Journalier",
        string? periodLabel = null)
    {
        var period = periodLabel ?? $"Journée du {reportDate:dd/MM/yyyy}";
        var active = reports.Where(r => r.HasActivity).ToList();
        var totalDistance = active.Sum(r => r.Summary.TotalDistanceKm);
        var totalDriving = active.Sum(r => r.Summary.TotalDrivingSeconds);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(26);
                page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink));

                // Brand header (repeats on each page).
                page.Header().Column(h =>
                {
                    h.Item().Background(Navy).Padding(13).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Calypso Belive").FontColor("#ffffff").FontSize(16).Bold();
                            c.Item().Text(societe.Name).FontColor("#cbd5e1").FontSize(10);
                        });
                        row.ConstantItem(210).Column(c =>
                        {
                            c.Item().AlignRight().Text(title).FontColor("#ffffff").FontSize(13).Bold();
                            c.Item().AlignRight().Text(period).FontColor("#cbd5e1").FontSize(9);
                        });
                    });
                    h.Item().Height(3).Background(Navy2);
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    col.Spacing(13);

                    // Fleet-level summary cards.
                    col.Item().Row(row =>
                    {
                        row.Spacing(8);
                        FleetCard(row, reports.Count.ToString(), "Véhicules");
                        FleetCard(row, active.Count.ToString(), "Actifs");
                        FleetCard(row, $"{totalDistance:N0} km", "Distance totale");
                        FleetCard(row, DailyFleetReportService.FormatDuration(totalDriving), "Conduite totale");
                    });

                    foreach (var r in reports
                                 .OrderByDescending(x => x.HasActivity)
                                 .ThenByDescending(x => x.Summary.TotalDistanceKm))
                    {
                        col.Item().Element(c => Vehicle(c, r));
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                    t.Span($"Calypso Belive — généré le {DateTime.UtcNow.AddHours(1):dd/MM/yyyy HH:mm}  •  Page ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void FleetCard(RowDescriptor row, string value, string label)
    {
        row.RelativeItem().Background(Bg).Padding(10).Column(c =>
        {
            c.Item().Text(value).FontSize(15).Bold().FontColor(Ink);
            c.Item().Text(label).FontSize(8).FontColor(Muted);
        });
    }

    private static void MiniCard(RowDescriptor row, string value, string label)
    {
        row.RelativeItem().Background(Bg).Padding(7).Column(c =>
        {
            c.Item().Text(value).FontSize(11).Bold().FontColor(Ink);
            c.Item().Text(label).FontSize(7.5f).FontColor(Muted);
        });
    }

    private static IContainer Cell(IContainer c) =>
        c.PaddingVertical(2).PaddingRight(6).BorderBottom(1).BorderColor(SoftLine);

    private static void Vehicle(IContainer container, DailyActivityReportDto r)
    {
        container.Border(1).BorderColor(Line).Column(col =>
        {
            // Vehicle header.
            col.Item().Background(Bg).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(t =>
                    {
                        t.Span(r.VehicleName).FontSize(12).Bold().FontColor(Ink);
                        if (!string.IsNullOrWhiteSpace(r.Plate))
                            t.Span($"   {r.Plate}").FontSize(9).FontColor(Muted);
                    });
                    if (!string.IsNullOrWhiteSpace(r.DriverName))
                        c.Item().Text($"Chauffeur : {r.DriverName}").FontSize(8.5f).FontColor(Muted);
                });
                row.ConstantItem(110).AlignRight().Text(r.HasActivity ? "Actif" : "À l'arrêt")
                    .FontSize(9).Bold().FontColor(r.HasActivity ? Green : "#94a3b8");
            });

            if (!r.HasActivity)
            {
                col.Item().Padding(10).Text("Véhicule à l'arrêt toute la journée.").FontSize(9).FontColor(Muted);
                if (!string.IsNullOrWhiteSpace(r.LastPosition?.Address))
                    col.Item().PaddingHorizontal(10).PaddingBottom(10)
                        .Text($"Dernière position : {r.LastPosition!.Address}").FontSize(8.5f).FontColor(Muted);
                return;
            }

            var s = r.Summary;

            // Summary cards.
            col.Item().Padding(10).Row(row =>
            {
                row.Spacing(6);
                MiniCard(row, s.TotalDrivingFormatted, "Conduite");
                MiniCard(row, $"{s.TotalStoppedFormatted} ({s.StopCount})", "Arrêts");
                MiniCard(row, $"{s.TotalDistanceKm:N0} km", "Distance");
                MiniCard(row, $"{s.MaxSpeedKph:N0} km/h", "Vitesse max");
                if (s.FuelRefillCount > 0)
                    MiniCard(row, $"{s.FuelRefillCount} plein(s)",
                        s.TotalFuelRefillLiters.HasValue ? $"{s.TotalFuelRefillLiters.Value:N0} L" : "Carburant");
            });

            // First start of the day.
            if (r.FirstStart != null)
                col.Item().PaddingHorizontal(10).PaddingBottom(4).Text(t =>
                {
                    t.Span("Premier démarrage : ").FontSize(8.5f).Bold().FontColor(Ink);
                    t.Span($"{Hm(r.FirstStart.Timestamp)} — {r.FirstStart.Address ?? "adresse inconnue"}")
                        .FontSize(8.5f).FontColor(Muted);
                });

            // Fuel events.
            if (r.FuelEvents is { Count: > 0 })
            {
                col.Item().PaddingHorizontal(10).PaddingTop(2)
                    .Text("Événements carburant").FontSize(9).Bold().FontColor(Ink);
                foreach (var fe in r.FuelEvents)
                {
                    var label = fe.EventType switch
                    {
                        "refuel" => "Remplissage",
                        "theft_alert" => "Alerte vol",
                        _ => fe.EventType
                    };
                    col.Item().PaddingHorizontal(10).Text(t =>
                    {
                        t.Span($"• {Hm(fe.Timestamp)}  {label} — {fe.FuelPercent}%").FontSize(8.5f).FontColor(Muted);
                        if (fe.RefuelAmount.HasValue)
                            t.Span($"  (+{fe.RefuelAmount.Value:N0} L)").FontSize(8.5f).FontColor(Green);
                    });
                }
            }

            // Activity timeline.
            col.Item().PaddingHorizontal(10).PaddingTop(6)
                .Text("Chronologie des activités").FontSize(9).Bold().FontColor(Ink);
            col.Item().PaddingHorizontal(10).PaddingBottom(10).Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(48);   // type
                    cd.ConstantColumn(88);   // heures
                    cd.ConstantColumn(60);   // durée
                    cd.RelativeColumn();      // détail / adresse
                });

                foreach (var a in r.Activities)
                {
                    var isDrive = a.Type == "drive";
                    var times = a.EndTime.HasValue ? $"{Hm(a.StartTime)}–{Hm(a.EndTime.Value)}" : Hm(a.StartTime);
                    // Trajets : pas de détail (km/vitesse) — seuls les arrêts affichent l'adresse.
                    var detail = isDrive ? "" : (a.StartLocation?.Address ?? "adresse inconnue");

                    table.Cell().Element(Cell).Text(isDrive ? "Trajet" : "Arrêt")
                        .FontSize(8.5f).Bold().FontColor(isDrive ? Green : Amber);
                    table.Cell().Element(Cell).Text(times).FontSize(8.5f).FontColor(Muted);
                    table.Cell().Element(Cell).Text(a.DurationFormatted).FontSize(8.5f).FontColor(Muted);
                    table.Cell().Element(Cell).Text(detail).FontSize(8.5f).FontColor(Muted);
                }
            });

            // Last known position.
            if (r.LastPosition != null)
                col.Item().BorderTop(1).BorderColor(Line).Padding(8).Text(t =>
                {
                    t.Span("Dernière position : ").FontSize(8.5f).Bold().FontColor(Ink);
                    t.Span($"{Hm(r.LastPosition.Timestamp)} — {(r.LastPosition.IgnitionOn ? "Contact ON" : "Contact OFF")}")
                        .FontSize(8.5f).FontColor(Muted);
                });
        });
    }
}
