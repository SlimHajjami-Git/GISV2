using FluentAssertions;
using GisAPI.Application.Features.DataPort;
using Xunit;

namespace GisAPI.Tests.Application.DataPort;

/// <summary>
/// Feuille « Dépenses » du port Excel (DEF-002 de la campagne de test GPA) :
/// libellés de type écrits par l'export et relus par l'import, et clés de
/// dédoublonnage qui rendent idempotent le réimport du classeur exporté.
/// </summary>
public class ExpenseImportRowTests
{
    private static readonly DateTime Jour = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("insurance", "Assurance")]
    [InlineData("amende", "Amende")]
    [InlineData("tax", "Vignette")]
    [InlineData("insurance_refund", "Remboursement assurance")]
    [InlineData("repair", "Réparation accident")]
    public void L_export_ecrit_le_libelle_de_l_ecran_Depenses(string code, string libelle)
    {
        ExpenseImportRow.TypeLabel(code).Should().Be(libelle);
        ExpenseImportRow.ParseType(libelle).Should().Be((code, (string?)null), "l'import relit le libellé exporté");
    }

    [Theory]
    [InlineData("ASSURANCE", "insurance")]
    [InlineData("peage", "peage")]
    [InlineData("toll", "peage")]
    [InlineData("Visite Technique", "technical_inspection")]
    [InlineData("amende", "amende")]
    public void L_import_accepte_code_synonyme_casse_et_accents(string saisi, string code)
    {
        ExpenseImportRow.ParseType(saisi).Type.Should().Be(code);
    }

    [Fact]
    public void Un_type_vide_devient_autre_et_un_type_inconnu_est_garde_avec_une_remarque()
    {
        ExpenseImportRow.ParseType("  ").Should().Be(("autre", (string?)null));

        var (type, note) = ExpenseImportRow.ParseType("lavage_pro");
        type.Should().Be("lavage_pro", "l'export écrit tel quel un code inconnu : l'aller-retour ne doit pas le changer");
        note.Should().Contain("non reconnu");
        ExpenseImportRow.TypeLabel("lavage_pro").Should().Be("lavage_pro");
    }

    [Fact]
    public void Seuls_maintenance_et_entretien_vont_dans_la_feuille_Entretiens()
    {
        ExpenseImportRow.IsMaintenance("maintenance").Should().BeTrue();
        ExpenseImportRow.IsMaintenance("entretien").Should().BeTrue();
        ExpenseImportRow.IsMaintenance("insurance").Should().BeFalse();
        ExpenseImportRow.IsMaintenance("amende").Should().BeFalse();
    }

    [Fact]
    public void La_cle_d_une_depense_ignore_casse_accents_synonymes_et_heure()
    {
        var enBase = ExpenseImportRow.NaturalKey(38, Jour, "assurance", 625m, "Renouvellement Assurance - AXA");
        var relue = ExpenseImportRow.NaturalKey(38, Jour.Date, "Assurance", 625.00m, "renouvellement assurance - axa");

        relue.Should().Be(enBase);
        ExpenseImportRow.NaturalKey(38, Jour, "insurance", 625m, "Autre police").Should().NotBe(enBase);
        ExpenseImportRow.NaturalKey(38, Jour, "amende", 625m, "Renouvellement Assurance - AXA").Should().NotBe(enBase);
        ExpenseImportRow.NaturalKey(37, Jour, "insurance", 625m, "Renouvellement Assurance - AXA").Should().NotBe(enBase);
    }

    [Fact]
    public void Un_entretien_sans_intitule_se_reconnait_au_reimport()
    {
        // L'import enregistre « Entretien » pour un intitulé vide : réimporter le même
        // fichier (cellule vide) doit retrouver la ligne, et l'export d'une dépense
        // sans description (cellule vide) aussi.
        var importe = ExpenseImportRow.NaturalKey(38, Jour, "maintenance", 350m, "Entretien");
        ExpenseImportRow.NaturalKey(38, Jour, "maintenance", 350m, "").Should().Be(importe);
        ExpenseImportRow.NaturalKey(38, Jour, "entretien", 350m, null).Should().Be(importe);
    }

    [Fact]
    public void La_cle_d_un_plein_distingue_deux_pleins_du_meme_jour()
    {
        var plein = ExpenseImportRow.FuelNaturalKey(39, Jour, 81m, 145.80m, 213_406);

        ExpenseImportRow.FuelNaturalKey(39, Jour.Date, 81.00m, 145.8m, 213_406).Should().Be(plein);
        ExpenseImportRow.FuelNaturalKey(39, Jour, 81m, 145.80m, 213_900).Should().NotBe(plein);
        ExpenseImportRow.FuelNaturalKey(39, Jour, 40m, 72m, null).Should().NotBe(plein);
    }
}
