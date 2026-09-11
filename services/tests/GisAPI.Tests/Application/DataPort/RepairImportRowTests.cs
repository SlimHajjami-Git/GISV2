using FluentAssertions;
using GisAPI.Application.Features.DataPort;
using GisAPI.Application.Features.Reports.Common;
using Xunit;

namespace GisAPI.Tests.Application.DataPort;

/// <summary>
/// Analyse d'une ligne de la feuille « Réparations » de l'import Excel (recette du
/// 11/09/2026 : les réparations n'étaient ni exportées ni importables). Les montants
/// « total seul » reprennent la forme des 9 réparations réelles de la société 14.
/// </summary>
public class RepairImportRowTests
{
    // ── Montants ────────────────────────────────────────────────────────────

    [Fact]
    public void Un_total_seul_passe_en_main_d_oeuvre()
    {
        var a = RepairImportRow.Analyze(null, null, 200m, "Terminée", "Freinage");

        a.IsValid.Should().BeTrue();
        a.LaborCost.Should().Be(200m);
        a.PartsCost.Should().Be(0m);
        a.TotalCost.Should().Be(200m);
        a.Notes.Should().BeEmpty();
        RepairImportRow.PartLine(a).Should().BeNull("sans pièces, aucune ligne de pièce n'est créée");
    }

    [Fact]
    public void Un_total_avec_main_d_oeuvre_et_pieces_a_zero_passe_aussi_en_main_d_oeuvre()
    {
        var a = RepairImportRow.Analyze(0m, 0m, 200m, null, null);

        a.LaborCost.Should().Be(200m);
        a.PartsCost.Should().Be(0m);
        a.TotalCost.Should().Be(200m);
        a.Notes.Should().BeEmpty();
    }

    [Fact]
    public void Main_d_oeuvre_et_pieces_font_le_total_et_les_pieces_une_ligne_de_piece()
    {
        var a = RepairImportRow.Analyze(80m, 120m, 200m, "Terminée", "Freinage");

        a.LaborCost.Should().Be(80m);
        a.PartsCost.Should().Be(120m);
        a.TotalCost.Should().Be(200m);
        a.Notes.Should().BeEmpty();

        var part = RepairImportRow.PartLine(a);
        part.Should().NotBeNull();
        part!.PartName.Should().Be(RepairImportRow.ImportedPartName);
        part.Quantity.Should().Be(1);
        part.UnitPrice.Should().Be(120m);
        part.Subtotal.Should().Be(120m);
    }

    [Fact]
    public void Un_total_different_de_la_somme_est_signale_et_la_somme_retenue()
    {
        var a = RepairImportRow.Analyze(80m, 120m, 250m, null, null);

        a.IsValid.Should().BeTrue();
        a.TotalCost.Should().Be(200m);
        a.Notes.Should().ContainSingle().Which.Should().Contain("total saisi");
    }

    [Fact]
    public void Un_ecart_d_un_centime_n_est_pas_signale()
    {
        var a = RepairImportRow.Analyze(80m, 120m, 200.01m, null, null);

        a.TotalCost.Should().Be(200m);
        a.Notes.Should().BeEmpty();
    }

    [Fact]
    public void Main_d_oeuvre_vide_est_deduite_du_total()
    {
        var a = RepairImportRow.Analyze(null, 120m, 200m, null, null);

        a.LaborCost.Should().Be(80m);
        a.PartsCost.Should().Be(120m);
        a.TotalCost.Should().Be(200m);
        a.Notes.Should().BeEmpty();
    }

    [Fact]
    public void Pieces_vides_sont_deduites_du_total()
    {
        var a = RepairImportRow.Analyze(80m, null, 200m, null, null);

        a.LaborCost.Should().Be(80m);
        a.PartsCost.Should().Be(120m);
        a.TotalCost.Should().Be(200m);
        RepairImportRow.PartLine(a)!.UnitPrice.Should().Be(120m);
    }

    [Fact]
    public void Sans_total_la_somme_fait_foi()
    {
        var a = RepairImportRow.Analyze(80m, 120m, null, null, null);

        a.TotalCost.Should().Be(200m);
        a.Notes.Should().BeEmpty();
    }

    [Fact]
    public void Aucun_montant_donne_une_reparation_a_zero()
    {
        var a = RepairImportRow.Analyze(null, null, null, null, null);

        a.IsValid.Should().BeTrue();
        a.TotalCost.Should().Be(0m);
        RepairImportRow.PartLine(a).Should().BeNull();
    }

    [Fact]
    public void Les_montants_sont_arrondis_au_centime()
    {
        var a = RepairImportRow.Analyze(80.004m, 119.996m, null, null, null);

        a.LaborCost.Should().Be(80.00m);
        a.PartsCost.Should().Be(120.00m);
        a.TotalCost.Should().Be(200.00m);
    }

    [Fact]
    public void Un_montant_negatif_ecarte_la_ligne()
    {
        var a = RepairImportRow.Analyze(-10m, null, 200m, null, null);

        a.IsValid.Should().BeFalse();
        a.Error.Should().Contain("négatif");
    }

    [Fact]
    public void Un_montant_hors_colonne_ecarte_la_ligne_au_lieu_de_faire_echouer_tout_l_import()
    {
        var a = RepairImportRow.Analyze(null, null, 150_000_000m, null, null);

        a.IsValid.Should().BeFalse();
        a.Error.Should().Contain("trop élevé");
    }

    // ── Statut ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Terminée", RepairImportRow.Completed)]
    [InlineData("TERMINE", RepairImportRow.Completed)]
    [InlineData("terminee", RepairImportRow.Completed)]
    [InlineData("completed", RepairImportRow.Completed)]
    [InlineData("En cours", RepairImportRow.InProgress)]
    [InlineData("in_progress", RepairImportRow.InProgress)]
    [InlineData("En attente", RepairImportRow.Pending)]
    [InlineData("pending", RepairImportRow.Pending)]
    [InlineData("Annulée", RepairImportRow.Cancelled)]
    [InlineData("annule", RepairImportRow.Cancelled)]
    [InlineData("cancelled", RepairImportRow.Cancelled)]
    public void Le_statut_est_lu_en_francais_comme_en_anglais(string saisi, string attendu)
    {
        var (status, note) = RepairImportRow.ParseStatus(saisi);

        status.Should().Be(attendu);
        note.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_statut_vide_vaut_terminee_sans_remarque(string? saisi)
    {
        var (status, note) = RepairImportRow.ParseStatus(saisi);

        status.Should().Be(RepairImportRow.Completed);
        note.Should().BeNull();
    }

    [Fact]
    public void Un_statut_inconnu_vaut_terminee_avec_une_remarque()
    {
        var a = RepairImportRow.Analyze(null, null, 200m, "Bof", null);

        a.IsValid.Should().BeTrue();
        a.Status.Should().Be(RepairImportRow.Completed);
        a.Notes.Should().ContainSingle().Which.Should().Contain("Bof");
    }

    [Theory]
    [InlineData(RepairImportRow.Pending)]
    [InlineData(RepairImportRow.InProgress)]
    [InlineData(RepairImportRow.Completed)]
    [InlineData(RepairImportRow.Cancelled)]
    public void Le_libelle_ecrit_par_l_export_est_relu_par_l_import(string status)
    {
        var label = RepairImportRow.StatusLabel(status);

        RepairImportRow.ParseStatus(label).Status.Should().Be(status);
    }

    [Fact]
    public void Une_reparation_annulee_ne_fait_pas_avancer_le_compteur()
    {
        RepairImportRow.Analyze(null, null, 200m, "Annulée", null).AdvancesMileage.Should().BeFalse();
        RepairImportRow.Analyze(null, null, 200m, "Terminée", null).AdvancesMileage.Should().BeTrue();
        RepairImportRow.Analyze(null, null, 200m, "En cours", null).AdvancesMileage.Should().BeTrue();
    }

    // ── Type ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Électrique", RepairTypeClassifier.Electrique)]
    [InlineData("MÉCANIQUE", RepairTypeClassifier.Mecanique)]
    [InlineData("freinage", RepairTypeClassifier.Freinage)]
    [InlineData("Pneumatique", RepairTypeClassifier.Pneumatique)]
    [InlineData("Carrosserie", RepairTypeClassifier.Carrosserie)]
    [InlineData("Autre", RepairTypeClassifier.Autre)]
    [InlineData("Autres", RepairTypeClassifier.Autre)]
    public void Le_type_accentue_ou_non_est_reconnu(string saisi, string attendu)
    {
        var (type, note) = RepairImportRow.ParseType(saisi);

        type.Should().Be(attendu);
        note.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Un_type_vide_reste_null_pour_etre_deduit_de_la_description(string? saisi)
    {
        var (type, note) = RepairImportRow.ParseType(saisi);

        type.Should().BeNull();
        note.Should().BeNull();
    }

    [Fact]
    public void Un_type_hors_liste_devient_autre_avec_une_remarque()
    {
        var (type, note) = RepairImportRow.ParseType("Vidange");

        type.Should().Be(RepairTypeClassifier.Autre);
        note.Should().Contain("Vidange");
    }

    [Theory]
    [InlineData(RepairTypeClassifier.Electrique)]
    [InlineData(RepairTypeClassifier.Mecanique)]
    [InlineData(RepairTypeClassifier.Freinage)]
    [InlineData(RepairTypeClassifier.Pneumatique)]
    [InlineData(RepairTypeClassifier.Carrosserie)]
    [InlineData(RepairTypeClassifier.Autre)]
    public void Le_libelle_de_type_ecrit_par_l_export_est_relu_sans_remarque(string type)
    {
        var (relu, note) = RepairImportRow.ParseType(RepairTypeClassifier.Label(type));

        relu.Should().Be(type);
        note.Should().BeNull();
    }

    // ── Dédoublonnage ───────────────────────────────────────────────────────

    [Fact]
    public void La_cle_naturelle_ignore_l_heure_la_casse_et_les_accents()
    {
        var enBase = RepairImportRow.NaturalKey(7, new DateTime(2026, 8, 18, 14, 30, 0, DateTimeKind.Utc), 200m, "Plaquettes de frein AV");
        var importee = RepairImportRow.NaturalKey(7, new DateTime(2026, 8, 18), 200.00m, "  PLAQUETTES de  frein av ");

        importee.Should().Be(enBase);
    }

    [Fact]
    public void La_cle_naturelle_distingue_vehicule_jour_montant_et_description()
    {
        var reference = RepairImportRow.NaturalKey(7, new DateTime(2026, 8, 18), 200m, "Plaquettes de frein AV");

        RepairImportRow.NaturalKey(8, new DateTime(2026, 8, 18), 200m, "Plaquettes de frein AV").Should().NotBe(reference);
        RepairImportRow.NaturalKey(7, new DateTime(2026, 8, 19), 200m, "Plaquettes de frein AV").Should().NotBe(reference);
        RepairImportRow.NaturalKey(7, new DateTime(2026, 8, 18), 210m, "Plaquettes de frein AV").Should().NotBe(reference);
        RepairImportRow.NaturalKey(7, new DateTime(2026, 8, 18), 200m, "Disques de frein AV").Should().NotBe(reference);
    }

    [Fact]
    public void La_reference_generee_suit_le_format_de_la_saisie_a_l_ecran()
    {
        RepairImportRow.GeneratedReference(new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc), 10)
            .Should().Be("REP-202609-0010");
    }

    // ── Chaînes ─────────────────────────────────────────────────────────────

    [Fact]
    public void Les_chaines_trop_longues_sont_tronquees_a_la_taille_de_la_colonne()
    {
        var longue = new string('x', 600);

        RepairImportRow.Truncate(longue, RepairImportRow.DescriptionMaxLength)!.Length.Should().Be(500);
        RepairImportRow.Truncate("REP-202609-0001", RepairImportRow.ReferenceMaxLength).Should().Be("REP-202609-0001");
        RepairImportRow.Truncate(null, 10).Should().BeNull();
    }

    [Fact]
    public void Le_nom_de_fournisseur_se_compare_sans_casse_ni_accents()
    {
        RepairImportRow.NormalizeKey("  Garage  ÉLITE ").Should().Be(RepairImportRow.NormalizeKey("garage elite"));
    }

    [Fact]
    public void La_troncature_ne_coupe_jamais_un_emoji_en_deux()
    {
        var texte = new string('a', 499) + "🚗" + "fin"; // voiture : paire de substitution aux positions 499-500
        var coupe = RepairImportRow.Truncate(texte, 500)!;
        coupe.Length.Should().Be(499);
        char.IsHighSurrogate(coupe[^1]).Should().BeFalse();
    }
}
