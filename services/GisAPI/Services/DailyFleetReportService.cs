using ClosedXML.Excel;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Queries.GetDailyActivityReport;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Daily fleet activity report mailer.
///
/// <para>Once per day, at or after 06:00 Tunisia local time (UTC+1), this
/// service builds — per société — a fleet activity summary for the previous
/// day and emails it to every user who opted in
/// (<see cref="User.DailyReportEmailEnabled"/> = true). Each email carries an
/// HTML summary body plus an Excel (.xlsx) attachment listing per-vehicle
/// activity.</para>
///
/// <para><b>Périmètre</b> : le contenu n'est PAS le même pour tout le monde. Il
/// est borné aux véhicules que chaque destinataire a le droit de voir, et un
/// destinataire qui n'en voit aucun ne reçoit rien du tout — voir
/// <see cref="LotsAsync"/>.</para>
///
/// <para><b>Timezone</b>: the platform stores GPS timestamps in UTC that are
/// actually Tunisia local (UTC+1) — the report query already shifts its window
/// by that offset. We compute "TN now" as <c>DateTime.UtcNow.AddHours(1)</c>
/// and report on <c>(TN now).Date.AddDays(-1)</c> (yesterday, TN).</para>
///
/// <para><b>Idempotence</b>: an in-memory <see cref="DateOnly"/> guard
/// (<c>_lastRunTnDate</c>) ensures the run fires at most once per TN calendar
/// day. The loop ticks every 60s and fires when the TN hour is ≥ 6 and we have
/// not yet run today. NOTE (v1 limitation): the guard is in-memory only, so a
/// process restart after 06:00 on the same day would resend the reports. This
/// is acceptable for v1.</para>
/// </summary>
public class DailyFleetReportService : BackgroundService
{
    private const int SendHourTn = 6;          // fire at/after 06:00 TN
    private const int TickSeconds = 60;        // re-evaluate every minute
    private const int StartupDelaySeconds = 30;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DailyFleetReportService> _logger;

    // Last TN calendar day we already sent reports for. Guards against
    // re-sending within the same day (in-memory only — see class remarks).
    private DateOnly _lastRunTnDate = DateOnly.MinValue;

    // Last TN day we attempted the WEEKLY recap (in-memory). The per-société DB
    // guard (Societe.LastWeeklyReportSentDate) prevents any resend across restarts.
    private DateOnly _lastWeeklyAttemptTnDate = DateOnly.MinValue;

    public DailyFleetReportService(
        IServiceProvider serviceProvider,
        ILogger<DailyFleetReportService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(StartupDelaySeconds), ct); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation(
            "DailyFleetReportService started (sends once per day at/after {Hour}:00 TN)",
            SendHourTn);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tnNow = DateTime.UtcNow.AddHours(1);
                var tnToday = DateOnly.FromDateTime(tnNow);

                if (tnNow.Hour >= SendHourTn)
                {
                    // Daily report — every day, covers the previous day.
                    if (_lastRunTnDate < tnToday)
                    {
                        // Mark before sending so a long/failing run never double-fires this day.
                        _lastRunTnDate = tnToday;
                        using var scope = _serviceProvider.CreateScope();
                        await SendAllAsync(scope, ct);
                    }

                    // Weekly recap — Mondays only, covers the previous Mon→Sun week.
                    if (tnNow.DayOfWeek == DayOfWeek.Monday && _lastWeeklyAttemptTnDate < tnToday)
                    {
                        _lastWeeklyAttemptTnDate = tnToday;
                        using var scope = _serviceProvider.CreateScope();
                        await SendWeeklyAsync(scope, ct);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "DailyFleetReportService cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(TickSeconds), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>
    /// Destinataires du rapport journalier ET hebdomadaire d'une société : les comptes actifs
    /// qui l'ont demandé, jamais un compte chauffeur. Un salarié passé chauffeur gardait
    /// sinon son abonnement (il ne peut plus le couper, /api/reports lui est fermé) :
    /// DriverAccountRules.Apply coupe déjà le drapeau, ce filtre est la ceinture, comme dans
    /// NotificationAudience.
    ///
    /// <para>Être destinataire ne dit RIEN de ce qu'on a le droit de voir : le contenu envoyé
    /// à chacun est borné à son périmètre véhicules par <see cref="LotsAsync"/>.</para>
    /// </summary>
    internal static Task<List<User>> RecipientsAsync(IGisDbContext context, int companyId, CancellationToken ct) =>
        context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId
                     && u.DailyReportEmailEnabled
                     && u.Status == "active"
                     && u.AccountType != UserAccountTypes.Driver
                     && u.Email != null
                     && u.Email != "")
            .ToListAsync(ct);

    /// <summary>
    /// Un lot d'envoi : un périmètre de véhicules, et les destinataires qui le partagent.
    /// Le contenu (PDF ou classeur) n'est calculé qu'UNE fois par lot, pas une fois par personne.
    /// </summary>
    internal sealed record LotRapport(int[] VehicleIds, List<User> Destinataires);

    /// <summary>
    /// Périmètre véhicules de CHAQUE destinataire, puis regroupement de ceux qui partagent
    /// exactement le même.
    ///
    /// <para>Constat : le service bâtissait le contenu sur tout le parc de la société et
    /// envoyait le MÊME fichier à tout le monde. Chez un loueur, dont chaque locataire a un
    /// compte restreint à ses propres véhicules, un locataire affecté à 2 véhicules recevait
    /// chaque matin l'activité des 307 du parc. Le cloisonnement par SOCIÉTÉ est un invariant
    /// EF qu'on ne peut pas oublier ; le cloisonnement par UTILISATEUR est une convention
    /// recopiée à la main — et elle avait été oubliée ici.</para>
    ///
    /// <para>Trois états, jamais confondus (même sémantique que
    /// <c>VehicleScope.AccessibleVehicleIdsAsync</c> et <see cref="DashboardService.ScopeIdsAsync"/>) :</para>
    /// <list type="bullet">
    ///   <item><description><c>null</c> = administrateur : tout le parc, <b>y compris quand il
    ///     n'a aucune affectation</b> (cas réel : une administratrice de loueur a 0 ligne dans
    ///     UserVehicles et doit continuer à tout voir) ;</description></item>
    ///   <item><description>liste non vide : ses véhicules, et eux seuls ;</description></item>
    ///   <item><description>liste VIDE = non-admin sans affectation : il ne voit rien, donc
    ///     <b>aucun envoi</b> — surtout pas le parc entier.</description></item>
    /// </list>
    /// </summary>
    internal static async Task<List<LotRapport>> LotsAsync(
        IGisDbContext context,
        List<User> destinataires,
        int[] vehiculesSociete,
        CancellationToken ct)
    {
        // Drapeau administrateur relu en base, comme NotificationAudience : administrateur de
        // société OU rôle système. Requête à part plutôt qu'un Include sur la navigation Role :
        // celle-ci est requise, et la jointure INTERNE écarterait en silence un destinataire
        // dont la ligne de rôle manque. Ici on veut l'inverse : il reste destinataire, simplement
        // sans le privilège « tout le parc ».
        var rolesAdmin = (await context.Roles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.IsCompanyAdmin || r.IsSystemRole)
            .Select(r => r.Id)
            .ToListAsync(ct)).ToHashSet();

        // Clé du lot = les identifiants de son périmètre. Les véhicules gardent l'ordre du parc,
        // donc deux destinataires de même périmètre produisent forcément la même clé.
        var lots = new Dictionary<string, LotRapport>();

        foreach (var user in destinataires)
        {
            var portee = await DashboardService.ScopeIdsAsync(
                context, rolesAdmin.Contains(user.RoleId), user.Id, ct);

            int[] vehicules;
            if (portee is null)
            {
                vehicules = vehiculesSociete;
            }
            else
            {
                // Intersection avec le parc : une affectation restée derrière un véhicule
                // supprimé ou déplacé ne doit jamais faire sortir du périmètre société.
                var affectes = portee.ToHashSet();
                vehicules = vehiculesSociete.Where(affectes.Contains).ToArray();
            }

            // Rien de visible : on n'envoie pas. Un rapport vide serait un envoi de trop, et
            // le parc entier serait exactement la fuite qu'on ferme.
            if (vehicules.Length == 0)
                continue;

            var cle = string.Join(',', vehicules);
            if (lots.TryGetValue(cle, out var lot))
                lot.Destinataires.Add(user);
            else
                lots[cle] = new LotRapport(vehicules, new List<User> { user });
        }

        return lots.Values.ToList();
    }

    /// <summary>
    /// Trace les destinataires écartés faute de véhicule visible : sans cette ligne, un client
    /// qui ne reçoit plus rien n'aurait aucune explication dans les journaux.
    /// </summary>
    private void JournaliserIgnores(string rapport, Societe societe, int destinataires, List<LotRapport> lots)
    {
        var ignores = destinataires - lots.Sum(l => l.Destinataires.Count);
        if (ignores > 0)
            _logger.LogInformation(
                "{Rapport} : {Ignores} destinataire(s) sans véhicule visible, aucun envoi pour eux (société {Company})",
                rapport, ignores, societe.Name);
    }

    internal async Task SendAllAsync(IServiceScope scope, CancellationToken ct)
    {
        // Yesterday, TN. The report handler shifts the day window by the UTC+1
        // offset internally, so we pass the TN calendar date here.
        var reportDateTn = DateTime.UtcNow.AddHours(1).Date.AddDays(-1);
        var reportDateOnly = DateOnly.FromDateTime(reportDateTn);

        var context = scope.ServiceProvider.GetRequiredService<IGisDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        // No HTTP tenant context here — always ignore the global query filters
        // and scope explicitly by CompanyId. Tracked (not AsNoTracking) so we can
        // persist the per-société "already sent" guard below.
        var societes = await context.Societes
            .IgnoreQueryFilters()
            .ToListAsync(ct);

        int companiesProcessed = 0;
        int emailsSent = 0;
        int emailsFailed = 0;

        foreach (var societe in societes)
        {
            try
            {
                // Persistent idempotence: skip if this société already received this
                // day's report (survives API restarts — no re-send on a deploy).
                if (societe.LastDailyReportSentDate.HasValue && societe.LastDailyReportSentDate.Value >= reportDateOnly)
                    continue;

                var users = await RecipientsAsync(context, societe.Id, ct);

                if (users.Count == 0)
                    continue;

                // Ordonné : l'ordre du parc rend les clés de lot stables (voir LotsAsync).
                var vehiculesSociete = await context.Vehicles
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(v => v.CompanyId == societe.Id)
                    .OrderBy(v => v.Id)
                    .Select(v => v.Id)
                    .ToArrayAsync(ct);

                if (vehiculesSociete.Length == 0)
                    continue;

                // Un contenu par PÉRIMÈTRE, plus un contenu unique par société.
                var lots = await LotsAsync(context, users, vehiculesSociete, ct);
                JournaliserIgnores("Rapport journalier", societe, users.Count, lots);

                if (lots.Count == 0)
                    continue;

                companiesProcessed++;

                var subject = $"Rapport journalier flotte {societe.Name} — {reportDateTn:dd/MM/yyyy}";
                var fileName = $"rapport-journalier-{reportDateTn:yyyy-MM-dd}.pdf";

                foreach (var lot in lots)
                {
                    var reports = await mediator.Send(
                        new GetDailyActivityReportsQuery(reportDateTn, lot.VehicleIds), ct);

                    var htmlBody = BuildHtmlBody(societe, reportDateTn, reports);
                    var pdfBytes = DailyFleetReportPdf.Build(societe, reportDateTn, reports);

                    foreach (var user in lot.Destinataires)
                    {
                        try
                        {
                            await emailService.SendEmailWithAttachmentAsync(
                                user.Email,
                                user.FullName,
                                subject,
                                htmlBody,
                                pdfBytes,
                                fileName,
                                "application/pdf",
                                ct);
                            emailsSent++;
                            _logger.LogInformation(
                                "Daily fleet report sent to {Email} ({Vehicles} véhicule(s), société {Company})",
                                user.Email, lot.VehicleIds.Length, societe.Name);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            emailsFailed++;
                            _logger.LogError(ex,
                                "Failed to send daily fleet report to {Email} (société {Company})",
                                user.Email, societe.Name);
                        }
                    }
                }

                // Persisted idempotence: mark this société done for this report date
                // so an API restart later the same day will not re-send.
                societe.LastDailyReportSentDate = reportDateOnly;
                await context.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to process daily fleet report for société {Company}",
                    societe.Name);
            }
        }

        _logger.LogInformation(
            "DailyFleetReportService: {Companies} compan(y/ies) processed, {Sent} email(s) sent, {Failed} failure(s) for report date {Date:dd/MM/yyyy}",
            companiesProcessed, emailsSent, emailsFailed, reportDateTn);
    }

    /// <summary>
    /// Weekly recap: on Monday (≥06:00 TN), aggregates the previous Monday→Sunday
    /// week per vehicle and emails it to the opted-in users (same opt-in flag as the
    /// daily report). The per-vehicle daily query is summed over the 7 days.
    /// </summary>
    internal async Task SendWeeklyAsync(IServiceScope scope, CancellationToken ct)
    {
        var tnNow = DateTime.UtcNow.AddHours(1);
        var weekEndTn = tnNow.Date.AddDays(-1);            // dimanche (la veille)
        var weekStartTn = weekEndTn.AddDays(-6);           // lundi
        var weekTag = DateOnly.FromDateTime(weekStartTn);  // clé anti-doublon (lundi de la semaine)

        var context = scope.ServiceProvider.GetRequiredService<IGisDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var societes = await context.Societes.IgnoreQueryFilters().ToListAsync(ct);
        int companiesProcessed = 0, emailsSent = 0, emailsFailed = 0;

        foreach (var societe in societes)
        {
            try
            {
                if (societe.LastWeeklyReportSentDate.HasValue && societe.LastWeeklyReportSentDate.Value >= weekTag)
                    continue;

                var users = await RecipientsAsync(context, societe.Id, ct);
                if (users.Count == 0)
                    continue;

                var vehiculesSociete = await context.Vehicles
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(v => v.CompanyId == societe.Id)
                    .OrderBy(v => v.Id)
                    .Select(v => v.Id)
                    .ToArrayAsync(ct);
                if (vehiculesSociete.Length == 0)
                    continue;

                // Même cloisonnement que le rapport journalier : le récap hebdomadaire part des
                // mêmes destinataires et doit s'arrêter aux mêmes véhicules.
                var lots = await LotsAsync(context, users, vehiculesSociete, ct);
                JournaliserIgnores("Rapport hebdomadaire", societe, users.Count, lots);

                if (lots.Count == 0)
                    continue;

                companiesProcessed++;

                var periodLabel = $"Semaine du {weekStartTn:dd/MM/yyyy} au {weekEndTn:dd/MM/yyyy}";
                var subject = $"Rapport hebdomadaire flotte {societe.Name} — {weekStartTn:dd/MM} au {weekEndTn:dd/MM/yyyy}";
                var fileName = $"rapport-hebdo-{weekStartTn:yyyy-MM-dd}_{weekEndTn:yyyy-MM-dd}.xlsx";

                foreach (var lot in lots)
                {
                    var reports = await AggregerSemaineAsync(
                        mediator, lot.VehicleIds, weekStartTn, weekEndTn, ct);

                    var htmlBody = BuildHtmlBody(societe, weekEndTn, reports, "Rapport hebdomadaire", periodLabel);
                    var excelBytes = BuildExcel(societe, weekEndTn, reports, "Rapport hebdomadaire flotte", periodLabel);

                    foreach (var user in lot.Destinataires)
                    {
                        try
                        {
                            await emailService.SendEmailWithAttachmentAsync(
                                user.Email,
                                user.FullName,
                                subject,
                                htmlBody,
                                excelBytes,
                                fileName,
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                ct);
                            emailsSent++;
                            _logger.LogInformation(
                                "Weekly fleet report sent to {Email} ({Vehicles} véhicule(s), société {Company})",
                                user.Email, lot.VehicleIds.Length, societe.Name);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            emailsFailed++;
                            _logger.LogError(ex,
                                "Failed to send weekly fleet report to {Email} (société {Company})",
                                user.Email, societe.Name);
                        }
                    }
                }

                societe.LastWeeklyReportSentDate = weekTag;
                await context.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to process weekly fleet report for société {Company}",
                    societe.Name);
            }
        }

        _logger.LogInformation(
            "WeeklyFleetReport: {Companies} compan(y/ies), {Sent} sent, {Failed} failed for week {Start:dd/MM}-{End:dd/MM/yyyy}",
            companiesProcessed, emailsSent, emailsFailed, weekStartTn, weekEndTn);
    }

    /// <summary>
    /// Somme des 7 jours, par véhicule, pour le périmètre passé. La requête de rapport est
    /// journalière : on la rejoue jour par jour et on agrège. Extrait de SendWeeklyAsync pour
    /// être rejoué tel quel sur CHAQUE périmètre (un récap par lot de destinataires).
    /// </summary>
    private static async Task<List<DailyActivityReportDto>> AggregerSemaineAsync(
        IMediator mediator,
        int[] vehicleIds,
        DateTime weekStartTn,
        DateTime weekEndTn,
        CancellationToken ct)
    {
        var agg = new Dictionary<int, DailyActivityReportDto>();
        for (var day = weekStartTn; day <= weekEndTn; day = day.AddDays(1))
        {
            var dayReports = await mediator.Send(
                new GetDailyActivityReportsQuery(day, vehicleIds), ct);
            foreach (var dr in dayReports)
            {
                if (!agg.TryGetValue(dr.VehicleId, out var acc))
                {
                    acc = new DailyActivityReportDto
                    {
                        VehicleId = dr.VehicleId,
                        VehicleName = dr.VehicleName,
                        Plate = dr.Plate,
                        DriverName = dr.DriverName,
                        ReportDate = weekEndTn,
                        HasActivity = false,
                        Summary = new DailySummaryDto()
                    };
                    agg[dr.VehicleId] = acc;
                }
                if (string.IsNullOrEmpty(acc.Plate) && !string.IsNullOrEmpty(dr.Plate)) acc.Plate = dr.Plate;
                if (string.IsNullOrEmpty(acc.DriverName) && !string.IsNullOrEmpty(dr.DriverName)) acc.DriverName = dr.DriverName;
                if (dr.HasActivity)
                {
                    acc.HasActivity = true;
                    acc.Summary.TotalDistanceKm += dr.Summary.TotalDistanceKm;
                    acc.Summary.TotalDrivingSeconds += dr.Summary.TotalDrivingSeconds;
                    acc.Summary.TotalStoppedSeconds += dr.Summary.TotalStoppedSeconds;
                    acc.Summary.TotalActiveSeconds += dr.Summary.TotalActiveSeconds;
                    acc.Summary.StopCount += dr.Summary.StopCount;
                    acc.Summary.DriveCount += dr.Summary.DriveCount;
                    acc.Summary.FuelRefillCount += dr.Summary.FuelRefillCount;
                    if (dr.Summary.MaxSpeedKph > acc.Summary.MaxSpeedKph)
                        acc.Summary.MaxSpeedKph = dr.Summary.MaxSpeedKph;
                }
            }
        }

        // Vitesse moyenne de la semaine = distance totale / temps de conduite total.
        foreach (var acc in agg.Values)
        {
            var hrs = acc.Summary.TotalDrivingSeconds / 3600.0;
            acc.Summary.AvgSpeedKph = hrs > 0 ? Math.Round(acc.Summary.TotalDistanceKm / hrs, 1) : 0;
        }

        return agg.Values.ToList();
    }

    public static string BuildHtmlBody(Societe societe, DateTime reportDate, List<DailyActivityReportDto> reports, string badgeLabel = "Rapport journalier", string? periodLabel = null)
    {
        var period = periodLabel ?? $"Journée du {reportDate:dd/MM/yyyy}";
        var active = reports.Where(r => r.HasActivity).ToList();
        var totalDistanceKm = active.Sum(r => r.Summary.TotalDistanceKm);
        var totalDrivingSeconds = active.Sum(r => r.Summary.TotalDrivingSeconds);

        var rows = new System.Text.StringBuilder();
        // Sort: vehicles with activity first, then by distance desc.
        foreach (var r in reports.OrderByDescending(r => r.HasActivity).ThenByDescending(r => r.Summary.TotalDistanceKm))
        {
            var rowBg = r.HasActivity ? "#ffffff" : "#f8fafc";
            var muted = r.HasActivity ? "#475569" : "#94a3b8";
            var distance = r.HasActivity ? $"{r.Summary.TotalDistanceKm:N1} km" : "—";
            var driving = r.HasActivity ? FormatDuration(r.Summary.TotalDrivingSeconds) : "—";
            var stops = r.HasActivity ? r.Summary.StopCount.ToString() : "—";
            var maxSpeed = r.HasActivity ? $"{r.Summary.MaxSpeedKph:N0} km/h" : "—";

            rows.Append($@"
        <tr style=""background:{rowBg};"">
          <td style=""padding:10px 12px;font-size:13px;color:#1e293b;border-bottom:1px solid #e2e8f0;"">{HtmlEncode(r.VehicleName)}</td>
          <td style=""padding:10px 12px;font-size:13px;color:{muted};border-bottom:1px solid #e2e8f0;"">{HtmlEncode(r.Plate ?? "—")}</td>
          <td style=""padding:10px 12px;font-size:13px;color:{muted};border-bottom:1px solid #e2e8f0;"">{HtmlEncode(r.DriverName ?? "—")}</td>
          <td style=""padding:10px 12px;font-size:13px;color:{muted};border-bottom:1px solid #e2e8f0;text-align:right;"">{distance}</td>
          <td style=""padding:10px 12px;font-size:13px;color:{muted};border-bottom:1px solid #e2e8f0;text-align:right;"">{driving}</td>
          <td style=""padding:10px 12px;font-size:13px;color:{muted};border-bottom:1px solid #e2e8f0;text-align:right;"">{stops}</td>
          <td style=""padding:10px 12px;font-size:13px;color:{muted};border-bottom:1px solid #e2e8f0;text-align:right;"">{maxSpeed}</td>
        </tr>");
        }

        return $@"
<!DOCTYPE html>
<html>
<head><meta charset=""utf-8""></head>
<body style=""margin:0;padding:0;background:#f1f5f9;font-family:Inter,system-ui,-apple-system,sans-serif;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f1f5f9;padding:40px 0;"">
    <tr><td align=""center"">
      <table width=""680"" cellpadding=""0"" cellspacing=""0"" style=""background:#fff;border-radius:12px;box-shadow:0 2px 8px rgba(0,0,0,0.06);overflow:hidden;"">
        <!-- Header -->
        <tr><td style=""background:linear-gradient(135deg,#1e3a5f 0%,#2d5a87 100%);padding:24px 30px;"">
          <table width=""100%""><tr>
            <td style=""color:#fff;font-size:20px;font-weight:700;"">Calypso Belive</td>
            <td align=""right""><span style=""background:rgba(255,255,255,0.2);color:#fff;padding:4px 12px;border-radius:12px;font-size:11px;font-weight:500;"">{badgeLabel}</span></td>
          </tr></table>
        </td></tr>
        <tr><td style=""height:4px;background:#2d5a87;""></td></tr>
        <!-- Title -->
        <tr><td style=""padding:28px 30px 8px;"">
          <h2 style=""margin:0;font-size:18px;color:#1e293b;font-weight:600;"">Activité de la flotte — {HtmlEncode(societe.Name)}</h2>
          <p style=""margin:6px 0 0;font-size:13px;color:#64748b;"">{period}</p>
        </td></tr>
        <!-- Summary cards -->
        <tr><td style=""padding:16px 30px 8px;"">
          <table width=""100%"" cellpadding=""0"" cellspacing=""0"">
            <tr>
              <td style=""padding:12px;background:#f8fafc;border-radius:8px;text-align:center;width:25%;"">
                <div style=""font-size:20px;font-weight:700;color:#1e293b;"">{reports.Count}</div>
                <div style=""font-size:11px;color:#64748b;"">Véhicules</div>
              </td>
              <td style=""width:8px;""></td>
              <td style=""padding:12px;background:#f8fafc;border-radius:8px;text-align:center;width:25%;"">
                <div style=""font-size:20px;font-weight:700;color:#1e293b;"">{active.Count}</div>
                <div style=""font-size:11px;color:#64748b;"">Actifs</div>
              </td>
              <td style=""width:8px;""></td>
              <td style=""padding:12px;background:#f8fafc;border-radius:8px;text-align:center;width:25%;"">
                <div style=""font-size:20px;font-weight:700;color:#1e293b;"">{totalDistanceKm:N0} km</div>
                <div style=""font-size:11px;color:#64748b;"">Distance totale</div>
              </td>
              <td style=""width:8px;""></td>
              <td style=""padding:12px;background:#f8fafc;border-radius:8px;text-align:center;width:25%;"">
                <div style=""font-size:20px;font-weight:700;color:#1e293b;"">{FormatDuration(totalDrivingSeconds)}</div>
                <div style=""font-size:11px;color:#64748b;"">Conduite totale</div>
              </td>
            </tr>
          </table>
        </td></tr>
        <!-- Per-vehicle table -->
        <tr><td style=""padding:16px 30px 8px;"">
          <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse:collapse;"">
            <thead>
              <tr style=""background:#f1f5f9;"">
                <th style=""padding:10px 12px;font-size:11px;color:#64748b;text-align:left;text-transform:uppercase;letter-spacing:0.5px;border-bottom:2px solid #e2e8f0;"">Véhicule</th>
                <th style=""padding:10px 12px;font-size:11px;color:#64748b;text-align:left;text-transform:uppercase;letter-spacing:0.5px;border-bottom:2px solid #e2e8f0;"">Plaque</th>
                <th style=""padding:10px 12px;font-size:11px;color:#64748b;text-align:left;text-transform:uppercase;letter-spacing:0.5px;border-bottom:2px solid #e2e8f0;"">Chauffeur</th>
                <th style=""padding:10px 12px;font-size:11px;color:#64748b;text-align:right;text-transform:uppercase;letter-spacing:0.5px;border-bottom:2px solid #e2e8f0;"">Distance</th>
                <th style=""padding:10px 12px;font-size:11px;color:#64748b;text-align:right;text-transform:uppercase;letter-spacing:0.5px;border-bottom:2px solid #e2e8f0;"">Conduite</th>
                <th style=""padding:10px 12px;font-size:11px;color:#64748b;text-align:right;text-transform:uppercase;letter-spacing:0.5px;border-bottom:2px solid #e2e8f0;"">Arrêts</th>
                <th style=""padding:10px 12px;font-size:11px;color:#64748b;text-align:right;text-transform:uppercase;letter-spacing:0.5px;border-bottom:2px solid #e2e8f0;"">Vitesse max</th>
              </tr>
            </thead>
            <tbody>{rows}</tbody>
          </table>
        </td></tr>
        <!-- Note -->
        <tr><td style=""padding:8px 30px 0;"">
          <p style=""margin:0;font-size:12px;color:#64748b;line-height:1.5;"">Le détail complet de chaque véhicule (chronologie, arrêts, carburant) est disponible dans le fichier joint à cet email.</p>
        </td></tr>
        <!-- Footer -->
        <tr><td style=""padding:28px 30px;border-top:1px solid #e2e8f0;margin-top:16px;"">
          <p style=""margin:0;font-size:11px;color:#94a3b8;line-height:1.5;"">
            Cet email a été envoyé automatiquement par la plateforme Calypso Belive.<br>
            &copy; {DateTime.UtcNow.Year} Belive Technologies — Système de gestion de flotte
          </p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";
    }

    public static byte[] BuildExcel(Societe societe, DateTime reportDate, List<DailyActivityReportDto> reports, string titlePrefix = "Rapport journalier flotte", string? periodLabel = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Synthèse");

        // Title rows.
        sheet.Cell(1, 1).Value = $"{titlePrefix} — {societe.Name}";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = periodLabel ?? $"Journée du {reportDate:dd/MM/yyyy}";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        // Header row.
        var headers = new[]
        {
            "Véhicule", "Plaque", "Chauffeur", "Distance (km)", "Temps conduite",
            "Temps arrêt", "Nb arrêts", "Vitesse max (km/h)", "Vitesse moy (km/h)", "Pleins"
        };
        const int headerRow = 4;
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var row = headerRow + 1;
        foreach (var r in reports.OrderByDescending(r => r.HasActivity).ThenByDescending(r => r.Summary.TotalDistanceKm))
        {
            var s = r.Summary;
            sheet.Cell(row, 1).Value = r.VehicleName;
            sheet.Cell(row, 2).Value = r.Plate ?? "";
            sheet.Cell(row, 3).Value = r.DriverName ?? "";

            if (r.HasActivity)
            {
                sheet.Cell(row, 4).Value = Math.Round(s.TotalDistanceKm, 1);
                sheet.Cell(row, 4).Style.NumberFormat.Format = "#,##0.0";
                sheet.Cell(row, 5).Value = FormatDuration(s.TotalDrivingSeconds);
                sheet.Cell(row, 6).Value = FormatDuration(s.TotalStoppedSeconds);
                sheet.Cell(row, 7).Value = s.StopCount;
                sheet.Cell(row, 8).Value = Math.Round(s.MaxSpeedKph, 0);
                sheet.Cell(row, 9).Value = Math.Round(s.AvgSpeedKph, 1);
                sheet.Cell(row, 9).Style.NumberFormat.Format = "#,##0.0";
                sheet.Cell(row, 10).Value = s.FuelRefillCount;
            }
            else
            {
                sheet.Cell(row, 4).Value = "—";
                sheet.Cell(row, 5).Value = "—";
                sheet.Cell(row, 6).Value = "—";
                sheet.Cell(row, 7).Value = "—";
                sheet.Cell(row, 8).Value = "—";
                sheet.Cell(row, 9).Value = "—";
                sheet.Cell(row, 10).Value = "—";
            }
            row++;
        }

        // Totals row (active vehicles only).
        var active = reports.Where(r => r.HasActivity).ToList();
        var totalsRow = row + 1;
        sheet.Cell(totalsRow, 1).Value = "TOTAL";
        sheet.Cell(totalsRow, 1).Style.Font.Bold = true;
        sheet.Cell(totalsRow, 4).Value = Math.Round(active.Sum(r => r.Summary.TotalDistanceKm), 1);
        sheet.Cell(totalsRow, 4).Style.NumberFormat.Format = "#,##0.0";
        sheet.Cell(totalsRow, 4).Style.Font.Bold = true;
        sheet.Cell(totalsRow, 5).Value = FormatDuration(active.Sum(r => r.Summary.TotalDrivingSeconds));
        sheet.Cell(totalsRow, 5).Style.Font.Bold = true;
        sheet.Cell(totalsRow, 6).Value = FormatDuration(active.Sum(r => r.Summary.TotalStoppedSeconds));
        sheet.Cell(totalsRow, 6).Style.Font.Bold = true;
        sheet.Cell(totalsRow, 7).Value = active.Sum(r => r.Summary.StopCount);
        sheet.Cell(totalsRow, 7).Style.Font.Bold = true;
        sheet.Cell(totalsRow, 10).Value = active.Sum(r => r.Summary.FuelRefillCount);
        sheet.Cell(totalsRow, 10).Style.Font.Bold = true;

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Formats a duration in seconds as e.g. "2h 15min" / "45min" / "30s".</summary>
    public static string FormatDuration(int seconds)
    {
        if (seconds <= 0) return "0min";
        if (seconds < 60) return $"{seconds}s";

        var hours = seconds / 3600;
        var minutes = (seconds % 3600) / 60;

        if (hours > 0)
            return minutes > 0 ? $"{hours}h {minutes}min" : $"{hours}h";

        return $"{minutes}min";
    }

    private static string HtmlEncode(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? "");
}
