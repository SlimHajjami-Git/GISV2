using FluentAssertions;
using GisAPI.Application.Services;
using Xunit;

namespace GisAPI.Tests.Application.Invoices;

/// <summary>
/// Carburant (19/09/2026) : le scan de facture va servir l'écran Carburant, qui a
/// besoin du VOLUME et du PRIX AU LITRE en plus du montant. Trois exigences :
/// lire ce qui est imprimé sans rien inventer, déduire la valeur manquante quand
/// les deux autres sont là, et SIGNALER un ticket dont les trois chiffres ne se
/// recoupent pas au lieu de le corriger en douce.
/// </summary>
public class InvoiceFuelVolumeTests
{
    [Fact]
    public void Le_volume_et_le_prix_au_litre_sont_lus_tels_qu_imprimes()
    {
        // Ticket AGIL : 41,55 L à 2,525 DT = 104,914 arrondi à 104,910 par la pompe.
        const string json = """
        {
          "supplierName": "STATION AGIL LA MARSA",
          "date": "12/09/2026",
          "amountTTC": "104,910",
          "currency": "TND",
          "category": "fuel",
          "liters": "41,55",
          "pricePerLiter": 2.525,
          "confidence": "high"
        }
        """;

        var r = InvoiceExtractionService.Parse(json);

        r.Liters.Should().Be(41.55m);
        r.PricePerLiter.Should().Be(2.525m);
        r.AmountTTC.Should().Be(104.910m);
        InvoiceExtractionService.CoherenceIssues(r)
            .Should().BeEmpty("l'écart pompe de 4 millimes est un arrondi, pas une erreur de lecture");
    }

    [Fact]
    public void Variantes_francaises_des_cles_acceptees()
    {
        const string json = """
        {"amountTTC": 100, "category": "fuel", "volume": 40, "prixLitre": "2,500"}
        """;

        var r = InvoiceExtractionService.Parse(json);

        r.Liters.Should().Be(40m);
        r.PricePerLiter.Should().Be(2.5m);
    }

    [Fact]
    public void Une_facture_qui_n_est_pas_du_carburant_ne_porte_ni_volume_ni_prix_au_litre()
    {
        const string json = """
        {"amountTTC": 342.5, "category": "maintenance", "description": "Vidange + filtres"}
        """;

        var r = InvoiceExtractionService.Parse(json);

        r.Liters.Should().BeNull();
        r.PricePerLiter.Should().BeNull();
        InvoiceExtractionService.CompleteFuelVolume(r).Should().BeSameAs(r, "rien à déduire de deux champs absents");
    }

    [Fact]
    public void Un_json_illisible_ne_fabrique_pas_de_volume()
    {
        var r = InvoiceExtractionService.Parse("{ ceci n'est pas du JSON");

        r.Liters.Should().BeNull();
        r.PricePerLiter.Should().BeNull();
        r.Confidence.Should().Be("low");
    }

    [Fact]
    public void Ticket_sans_litres_le_volume_se_deduit_du_prix_au_litre_et_du_total()
    {
        // Pompe qui n'imprime que le prix au litre et le total.
        var x = Fuel(total: 104.910m, liters: null, pricePerLiter: 2.525m);

        var complete = InvoiceExtractionService.CompleteFuelVolume(x);

        complete.Liters.Should().Be(41.55m);          // 104,910 / 2,525 = 41,549… → 41,55
        complete.PricePerLiter.Should().Be(2.525m);
        complete.AmountTTC.Should().Be(104.910m, "le total imprimé n'est jamais retouché");
    }

    [Fact]
    public void Ticket_sans_prix_au_litre_le_prix_se_deduit_du_volume_et_du_total()
    {
        var x = Fuel(total: 100m, liters: 40m, pricePerLiter: null);

        var complete = InvoiceExtractionService.CompleteFuelVolume(x);

        complete.PricePerLiter.Should().Be(2.5m);
        complete.Liters.Should().Be(40m);
    }

    [Fact]
    public void Sans_total_lisible_rien_n_est_deduit()
    {
        var x = new InvoiceExtraction(null, null, null, null, null, null, "TND", "fuel", null, null, "low",
            null, false, null, 2.525m);

        InvoiceExtractionService.CompleteFuelVolume(x).Liters.Should().BeNull();
    }

    [Fact]
    public void Volume_et_prix_deja_presents_ne_sont_jamais_recalcules()
    {
        // Les trois se recoupent : on ne retouche NI le volume NI le prix, même si
        // total / volume donnerait une troisième décimale différente.
        var x = Fuel(total: 104.910m, liters: 41.55m, pricePerLiter: 2.525m);

        var complete = InvoiceExtractionService.CompleteFuelVolume(x);

        complete.Liters.Should().Be(41.55m);
        complete.PricePerLiter.Should().Be(2.525m);
    }

    [Fact]
    public void Un_ticket_dont_les_trois_valeurs_ne_se_recoupent_pas_est_signale_et_laisse_intact()
    {
        // 30 L × 2,525 = 75,750, très loin des 104,910 imprimés : un des trois
        // chiffres est mal lu. On le dit (passe corrective + confiance dégradée)
        // sans rien réécrire — l'utilisateur voit les valeurs du document.
        var x = Fuel(total: 104.910m, liters: 30m, pricePerLiter: 2.525m);

        InvoiceExtractionService.CoherenceIssues(x)
            .Should().ContainSingle(i => i.Contains("pricePerLiter"));

        var complete = InvoiceExtractionService.CompleteFuelVolume(x);
        complete.Liters.Should().Be(30m);
        complete.PricePerLiter.Should().Be(2.525m);
        complete.AmountTTC.Should().Be(104.910m);
    }

    [Fact]
    public void Un_ticket_sans_TTC_mais_avec_HT_est_verifie_sur_le_HT()
    {
        // Beaucoup de tickets de station n'impriment qu'un seul montant.
        var x = new InvoiceExtraction(null, null, null, 104.910m, null, null, "TND", "fuel", null, null, "high",
            null, false, 30m, 2.525m);

        InvoiceExtractionService.CoherenceIssues(x).Should().ContainSingle(i => i.Contains("ne retombe pas sur le total"));
    }

    [Fact]
    public void Un_avoir_carburant_garde_son_volume_et_son_prix_au_litre()
    {
        var avoir = new InvoiceExtraction(null, null, null, null, null, -104.910m, "TND", "fuel", null, null, "high",
            null, false, 41.55m, 2.525m);

        var r = InvoiceExtractionService.AsCreditNoteIfDetected(avoir);

        r.IsCreditNote.Should().BeTrue();
        r.AmountTTC.Should().Be(104.910m);
        r.Liters.Should().Be(41.55m);
        r.PricePerLiter.Should().Be(2.525m);
    }

    // ── 19/09/2026 : la complétion n'est plus aveugle ─────────────────────────
    // Deux trous relevés à la relecture du scan devenu brique partagée :
    //   • elle n'était gardée par AUCUN contrôle de catégorie, alors que Parse accepte
    //     « volume » comme synonyme de litres ;
    //   • son garde-fou « la valeur déduite doit retomber sur le total » était
    //     TAUTOLOGIQUE — la valeur est calculée à partir de ce même total, l'écart se
    //     réduit à l'arrondi et le contrôle ne refusait jamais rien.

    [Fact]
    public void Une_facture_de_garage_avec_5_L_d_huile_ne_devient_pas_un_plein()
    {
        // « Huile 5W40 — 5 L » : le modèle range les 5 litres sous « volume », que Parse
        // accepte comme synonyme de litres. La complétion en tirait un prix au litre de
        // 342,500 / 5 = 68,500 DT et l'écran Carburant se voyait proposer ce plein fantôme.
        const string json = """
        {
          "supplierName": "GARAGE EL AMEN",
          "amountTTC": "342,500",
          "category": "maintenance",
          "description": "Vidange + filtres",
          "volume": 5,
          "items": [
            { "label": "Huile 5W40 5 L", "amount": "215,000", "category": "maintenance" },
            { "label": "Filtre à huile", "amount": "127,500", "category": "maintenance" }
          ]
        }
        """;

        var r = InvoiceExtractionService.Parse(json);

        // Relecture du 19/09/2026 : la valeur est coupée À LA SOURCE. Ne plus la DÉDUIRE
        // ne suffisait pas — les 5 L bruts sortaient quand même du service, et l'écran
        // Carburant les lisait tels quels.
        r.Liters.Should().BeNull("hors carburant, le volume ne sort pas du service");
        r.PricePerLiter.Should().BeNull();

        var complete = InvoiceExtractionService.CompleteFuelVolume(r);

        complete.PricePerLiter.Should().BeNull("une facture d'entretien n'a pas de prix au litre");
        complete.Should().BeSameAs(r, "rien n'est retouché hors du carburant");
    }

    [Theory]
    [InlineData("maintenance")]
    [InlineData("repair")]
    [InlineData("insurance")]
    [InlineData("tax")]
    [InlineData("toll")]
    [InlineData("parking")]
    [InlineData("fine")]
    [InlineData("other")]
    public void Aucune_categorie_hors_carburant_ne_rend_de_litres_ni_de_prix_au_litre(string categorie)
    {
        // Règle produit du 19/09/2026 : « les litres et le prix au litre, tu les ajoutes
        // juste à l'écran carburant ». Elle vaut pour TOUTES les autres catégories, y
        // compris celles qui facturent réellement du liquide (huile, lave-glace).
        var json = $$"""
        {"amountTTC": 342.5, "category": "{{categorie}}", "liters": 5, "pricePerLiter": 12.5}
        """;

        var r = InvoiceExtractionService.Parse(json);

        r.Category.Should().Be(categorie);
        r.Liters.Should().BeNull();
        r.PricePerLiter.Should().BeNull();
    }

    [Fact]
    public void Une_categorie_absente_ou_illisible_ne_rend_pas_de_litres()
    {
        // NormalizeCategory ramène l'inconnu à « other » : ce n'est pas du carburant
        // ATTESTÉ, donc rien ne sort. Le montant et le reste de la lecture, eux, restent.
        var r = InvoiceExtractionService.Parse("""
        {"amountTTC": 104.910, "volume": 41.55, "prixLitre": 2.525}
        """);

        r.Liters.Should().BeNull();
        r.PricePerLiter.Should().BeNull();
        r.AmountTTC.Should().Be(104.910m, "seuls les deux champs carburant sont coupés");
    }

    [Fact]
    public void Un_ticket_de_station_garde_volume_et_prix_au_litre()
    {
        // Le cas limite dans l'autre sens : la coupe ne doit rien retirer au carburant.
        var r = InvoiceExtractionService.Parse("""
        {"amountTTC": 104.910, "category": "fuel", "volume": 41.55, "prixLitre": 2.525}
        """);

        r.Liters.Should().Be(41.55m);
        r.PricePerLiter.Should().Be(2.525m);
    }

    [Fact]
    public void Un_avoir_de_station_lu_sur_le_document_garde_son_volume()
    {
        // L'avoir n'est réécrit en « credit_note » qu'APRÈS Parse : à la lecture, le
        // document est encore « fuel » et la coupe ne l'atteint pas.
        var r = InvoiceExtractionService.Parse("""
        {"amountTTC": -104.910, "category": "fuel", "liters": 41.55, "pricePerLiter": 2.525,
         "isCreditNote": true}
        """);

        r.Liters.Should().Be(41.55m);

        var positif = InvoiceExtractionService.AsCreditNoteIfDetected(r);
        positif.Category.Should().Be("credit_note");
        positif.Liters.Should().Be(41.55m, "la réécriture de la catégorie ne vide pas le volume déjà lu");
    }

    [Fact]
    public void Un_ticket_avec_timbre_fiscal_ne_fait_pas_deduire_de_volume()
    {
        // 41,55 L à 2,525 = 104,910, plus 0,600 DT de TIMBRE FISCAL : le total imprimé
        // (105,510) ne paie pas que du carburant. 105,510 / 2,525 = 41,79 L, soit 0,24 L
        // inventés — et l'ancien garde-fou les acceptait, puisque 41,79 × 2,525 retombe
        // évidemment sur 105,510.
        var ticket = new InvoiceExtraction("STATION SHELL", null, "2026-09-12",
            88.160m, 16.750m, 105.510m, "TND", "fuel", null, null, "high",
            null, false, null, 2.525m);

        InvoiceExtractionService.CompleteFuelVolume(ticket).Liters
            .Should().BeNull("TTC ≠ HT + TVA : le total porte autre chose que le plein");
    }

    [Fact]
    public void Un_ticket_dont_le_total_est_pur_se_complete_toujours()
    {
        // Même ticket sans timbre : HT + TVA = TTC, la déduction reste ouverte.
        var ticket = new InvoiceExtraction("STATION SHELL", null, "2026-09-12",
            88.160m, 16.750m, 104.910m, "TND", "fuel", null, null, "high",
            null, false, null, 2.525m);

        InvoiceExtractionService.CompleteFuelVolume(ticket).Liters.Should().Be(41.55m);
    }

    [Fact]
    public void Une_ligne_etrangere_au_carburant_arrete_la_deduction()
    {
        // Ticket de station qui facture aussi un lavage : le total couvre les deux.
        var ticket = new InvoiceExtraction("STATION AGIL", null, "2026-09-12",
            null, null, 119.910m, "TND", "fuel", null, null, "high",
            new List<InvoiceLineItem>
            {
                new("Gasoil 50", 104.910m, "fuel"),
                new("Lavage", 15.000m, "other")
            },
            false, null, 2.525m);

        InvoiceExtractionService.CompleteFuelVolume(ticket).Liters
            .Should().BeNull("15 DT de lavage feraient 5,94 L de gasoil imaginaires");
    }

    [Fact]
    public void Un_detail_entierement_carburant_laisse_la_deduction_passer()
    {
        var ticket = new InvoiceExtraction("STATION AGIL", null, "2026-09-12",
            null, null, 104.910m, "TND", "fuel", null, null, "high",
            new List<InvoiceLineItem> { new("Gasoil 50", 104.910m, "fuel") },
            false, null, 2.525m);

        InvoiceExtractionService.CompleteFuelVolume(ticket).Liters.Should().Be(41.55m);
    }

    [Fact]
    public void Une_ligne_sans_categorie_est_traitee_comme_etrangere()
    {
        // On ne déduit pas d'un détail qu'on ne comprend pas : mieux vaut un champ vide,
        // que l'utilisateur complète en lisant son ticket, qu'un volume faux.
        var ticket = new InvoiceExtraction(null, null, "2026-09-12",
            null, null, 104.910m, "TND", "fuel", null, null, "high",
            new List<InvoiceLineItem> { new("Ligne illisible", 104.910m, null) },
            false, null, 2.525m);

        InvoiceExtractionService.CompleteFuelVolume(ticket).Liters.Should().BeNull();
    }

    [Fact]
    public void Un_avoir_de_station_se_complete_sur_la_categorie_lue_sur_le_document()
    {
        // L'ORDRE compte : la mise au positif de l'avoir tourne AVANT la complétion et
        // réécrit la catégorie en « credit_note ». La catégorie à regarder est donc celle
        // que l'IA a lue sur le document, que l'appelant met de côté avant cette réécriture.
        var avoir = new InvoiceExtraction(null, null, null, null, null, -104.910m, "TND", "fuel", null, null, "high",
            null, false, null, 2.525m);

        var positif = InvoiceExtractionService.AsCreditNoteIfDetected(avoir);
        positif.Category.Should().Be("credit_note");

        InvoiceExtractionService.CompleteFuelVolume(positif).Liters
            .Should().BeNull("sur la seule catégorie réécrite, il n'y a plus rien à compléter");
        InvoiceExtractionService.CompleteFuelVolume(positif, avoir.Category).Liters
            .Should().Be(41.55m, "avec la catégorie du document, l'avoir de station garde son volume");
    }

    private static InvoiceExtraction Fuel(decimal total, decimal? liters, decimal? pricePerLiter) =>
        new(null, null, "2026-09-12", null, null, total, "TND", "fuel", null, null, "high",
            null, false, liters, pricePerLiter);
}
