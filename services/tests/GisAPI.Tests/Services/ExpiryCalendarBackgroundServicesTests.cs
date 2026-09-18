using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Rappels et score santé alignés sur les jours calendaires de l'écran
/// Échéances (ExpiryCalendar) — recette GPA, DEF-035.
///
/// <para>Constats : le moniteur d'échéances prenait le jour LOCAL de l'instant
/// relu par Npgsql legacy (fenêtre de rappel et clé de déduplication décalées
/// d'un jour hors UTC) ; les alertes prédictives et le score santé comparaient
/// des instants, si bien qu'une assurance au 20/09 « expirait dans 6 jours » le
/// 13/09 à 10:00 (l'écran en affiche 7) et qu'un document était « expiré » dès
/// le jour même de son échéance. Horloge figée : aucun test ne lit l'heure réelle.</para>
/// </summary>
public class ExpiryCalendarBackgroundServicesTests
{
    // « Aujourd'hui » = 13/09/2026 ; « maintenant » = 13/09/2026 10:00 UTC.
    private static readonly DateTime Today = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Today.AddHours(10);

    private static DateTime Utc(int month, int day, int h = 0, int m = 0, int s = 0) =>
        new(2026, month, day, h, m, s, DateTimeKind.Utc);

    /// <summary>Instant UTC relu comme Npgsql legacy : Kind=Local, dans le fuseau de la machine.</summary>
    private static DateTime LuEnHeureLocale(DateTime utc) => new DateTimeOffset(utc).LocalDateTime;

    // ── DocumentExpiryMonitoringService : fenêtre de rappel et déduplication ──

    [FactMachineHorsUtc]
    public void Moniteur_fenetre_et_cle_sur_le_jour_UTC_d_une_echeance_relue_en_heure_locale()
    {
        foreach (var stored in new[] { Utc(9, 20), Utc(9, 20, 23, 59, 59) })
        {
            var relue = LuEnHeureLocale(stored);

            // Rappel à 7 jours : la fenêtre ouvre le 13/09 (20 - 7).
            var reminder = DocumentExpiryMonitoringService.ReminderFor(relue, reminderDays: 7, Today);
            reminder.Should().NotBeNull("la fenêtre de 7 jours d'une échéance au 20/09 est ouverte le 13/09 ({0:O})", stored);
            reminder!.Value.ExpiryDay.Should().Be(Utc(9, 20));
            reminder.Value.ExpiryDay.Kind.Should().Be(DateTimeKind.Utc);
            reminder.Value.DaysRemaining.Should().Be(7);

            DocumentExpiryMonitoringService.DedupKey(44, "insurance", reminder.Value.ExpiryDay)
                .Should().Be("44|insurance|2026-09-20", "la clé doit rester la même quelle que soit l'heure stockée");

            // Rappel à 6 jours : la fenêtre n'ouvre que le 14/09.
            DocumentExpiryMonitoringService.ReminderFor(relue, reminderDays: 6, Today).Should().BeNull();
        }
    }

    [Fact]
    public void Moniteur_compte_les_jours_calendaires_et_borne_le_retard_a_60_jours()
    {
        DocumentExpiryMonitoringService.ReminderFor(Utc(9, 20, 23, 59, 59), 30, Now)!.Value.DaysRemaining.Should().Be(7);
        DocumentExpiryMonitoringService.ReminderFor(Utc(9, 13), 30, Now)!.Value.DaysRemaining.Should().Be(0);

        var echueDepuis60 = DocumentExpiryMonitoringService.ReminderFor(Utc(7, 15, 23, 59, 59), 30, Today);
        echueDepuis60!.Value.DaysRemaining.Should().Be(-60);
        DocumentExpiryMonitoringService.ReminderFor(Utc(7, 14, 12), 30, Today).Should().BeNull("au-delà de 60 jours de retard on cesse de relancer");

        // Rappel de 0 jour en base : au moins la veille, comme avant.
        DocumentExpiryMonitoringService.ReminderFor(Utc(9, 14), 0, Today).Should().NotBeNull();
        DocumentExpiryMonitoringService.ReminderFor(Utc(9, 15), 0, Today).Should().BeNull();
    }

    // ── PredictiveAlertService : documents véhicule et permis ────────────────

    [Fact]
    public void Alerte_predictive_annonce_les_jours_de_l_ecran_Echeances()
    {
        var assurance = PredictiveAlertService.DocumentExpiryAlert("Assurance", "QA-44", "QA-044", Utc(9, 20), Now);
        assurance.Should().Be(("Assurance expire dans 7 jour(s) pour QA-44 (QA-044)", "urgent"),
            "au 13/09 10:00, l'échéance du 20/09 est à 7 jours calendaires (6 en instants tronqués)");

        PredictiveAlertService.DocumentExpiryAlert("Vignette", "QA-44", "QA-044", Utc(9, 20, 23, 59, 59), Now)!
            .Value.Message.Should().Be("Vignette expire dans 7 jour(s) pour QA-44 (QA-044)");

        var permis = PredictiveAlertService.DriverPermitExpiryAlert("QA Un", Utc(9, 20), Now);
        permis.Should().Be(("Permis conducteur expire dans 7 jour(s) pour QA Un", "urgent"));
    }

    [Fact]
    public void Alerte_predictive_le_jour_de_l_echeance_n_est_pas_expiree()
    {
        PredictiveAlertService.DocumentExpiryAlert("Assurance", "QA-44", "QA-044", Utc(9, 13), Now)
            .Should().Be(("Assurance expire aujourd'hui pour QA-44 (QA-044)", "urgent"));
        PredictiveAlertService.DriverPermitExpiryAlert("QA Un", Utc(9, 13), Now)
            .Should().Be(("Permis conducteur expire aujourd'hui pour QA Un", "urgent"));

        // Échue la veille (même saisie tardive) : expirée, datée du jour de l'échéance.
        PredictiveAlertService.DocumentExpiryAlert("Assurance", "QA-44", "QA-044", Utc(9, 12, 23, 59, 59), Now)
            .Should().Be(("Assurance expiré(e) pour QA-44 (QA-044) depuis le 12/09/2026", "urgent"));
        PredictiveAlertService.DriverPermitExpiryAlert("QA Un", Utc(9, 12, 23, 59, 59), Now)
            .Should().Be(("Permis conducteur expiré pour QA Un depuis le 12/09/2026", "urgent"));
    }

    [Theory]
    [InlineData(15, "urgent")]
    [InlineData(16, "normal")]
    [InlineData(30, "normal")]
    [InlineData(31, null)]
    public void Alerte_predictive_garde_ses_seuils_de_15_et_30_jours(int days, string? priority)
    {
        var expiry = Today.AddDays(days);   // minuit UTC, comme la fiche véhicule et le renouvellement

        PredictiveAlertService.DocumentExpiryAlert("Assurance", "QA-44", "QA-044", expiry, Now)?.Priority.Should().Be(priority);
        PredictiveAlertService.DriverPermitExpiryAlert("QA Un", expiry, Now)?.Priority.Should().Be(priority);
        (PredictiveAlertService.DocumentExpiryAlert("Assurance", "QA-44", "QA-044", expiry, Now) is null).Should().Be(priority is null);
        (PredictiveAlertService.DriverPermitExpiryAlert("QA Un", expiry, Now) is null).Should().Be(priority is null);
    }

    [FactMachineHorsUtc]
    public void Alerte_predictive_date_le_jour_UTC_d_une_echeance_relue_en_heure_locale()
    {
        var relue = LuEnHeureLocale(Utc(9, 10, 23, 59, 59));

        PredictiveAlertService.DocumentExpiryAlert("Assurance", "QA-44", "QA-044", relue, Now)!
            .Value.Message.Should().Be("Assurance expiré(e) pour QA-44 (QA-044) depuis le 10/09/2026");
        PredictiveAlertService.DriverPermitExpiryAlert("QA Un", LuEnHeureLocale(Utc(9, 20, 23, 59, 59)), Now)!
            .Value.Message.Should().Be("Permis conducteur expire dans 7 jour(s) pour QA Un");
    }

    // ── VehicleHealthScoreService : facteur Documents ────────────────────────

    [Fact]
    public void Score_sante_un_document_du_jour_n_est_pas_expire()
    {
        var warnings = new List<string>();
        var vehicle = new Vehicle
        {
            Id = 1, Name = "QA-44",
            InsuranceExpiry = Utc(9, 13),                         // échéance aujourd'hui : bientôt, pas expirée
            TechnicalInspectionExpiry = Utc(9, 12, 23, 59, 59),   // échue la veille : expirée
            TaxExpiry = Utc(9, 13)                                // vignette du jour : pas expirée
        };

        var score = VehicleHealthScoreService.DocumentScore(vehicle, Now, warnings);

        score.Should().Be(15 - 2 - 5);
        warnings.Should().Equal("Assurance expire le 13/09/2026", "Contrôle technique expiré");
    }

    [Theory]
    [InlineData(30, 13)]
    [InlineData(31, 15)]
    public void Score_sante_garde_le_seuil_de_30_jours(int days, int expected)
    {
        var warnings = new List<string>();
        var vehicle = new Vehicle { Id = 1, Name = "QA-44", InsuranceExpiry = Today.AddDays(days) };

        VehicleHealthScoreService.DocumentScore(vehicle, Now, warnings).Should().Be(expected);
        warnings.Should().HaveCount(expected == 15 ? 0 : 1);
    }

    [FactMachineHorsUtc]
    public void Score_sante_date_l_avertissement_au_jour_UTC_d_une_echeance_relue_en_heure_locale()
    {
        var warnings = new List<string>();
        var vehicle = new Vehicle { Id = 1, Name = "QA-44", InsuranceExpiry = LuEnHeureLocale(Utc(9, 20, 23, 59, 59)) };

        VehicleHealthScoreService.DocumentScore(vehicle, Now, warnings).Should().Be(13);
        warnings.Should().Equal("Assurance expire le 20/09/2026");
    }
}
