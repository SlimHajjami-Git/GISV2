using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Ménage des factures scannées (<see cref="InvoiceOrphanCleanupService"/>).
///
/// <para>Constat du 19/09/2026 : le service balaie <c>uploads/invoices</c> toutes les 6
/// heures et supprime, passé 24 h, tout fichier que plus rien ne référence. Il a été écrit
/// quand le scan n'existait QUE sur l'écran Dépenses et ne regardait donc que
/// <c>vehicle_costs.receipt_url</c>. Le scan est depuis une brique partagée : Carburant et
/// Nouvelle réparation, faute de colonne dédiée, rangent le lien dans leurs NOTES. Leurs
/// factures scannées étaient détruites le LENDEMAIN, en silence, la ligne restant en base
/// avec un lien mort.</para>
///
/// <para>Ce que ces tests fixent : un fichier référencé par N'IMPORTE LEQUEL des porteurs
/// survit, un vrai orphelin est toujours supprimé, et le délai de grâce protège la revue
/// en cours — celle qui est à l'écran et que personne n'a encore enregistrée.</para>
/// </summary>
public class MenageFacturesScanneesTests : IDisposable
{
    private const int CompanyId = 7;

    /// <summary>Dossier temporaire qui tient lieu de ContentRootPath de l'API.</summary>
    private readonly string _racine = Path.Combine(Path.GetTempPath(), "gisv2-menage-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_racine)) Directory.Delete(_racine, recursive: true); } catch { /* ménage du ménage */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>Écrit un fichier comme le ferait le scan et rend son URL publique.</summary>
    private string Depose(string nom, TimeSpan age)
    {
        var dossier = Path.Combine(_racine, "uploads", "invoices", CompanyId.ToString());
        Directory.CreateDirectory(dossier);
        var chemin = Path.Combine(dossier, nom);
        File.WriteAllText(chemin, "facture");
        File.SetLastWriteTimeUtc(chemin, DateTime.UtcNow - age);
        return $"/uploads/invoices/{CompanyId}/{nom}";
    }

    private string Chemin(string nom) => Path.Combine(_racine, "uploads", "invoices", CompanyId.ToString(), nom);

    /// <summary>Balayage avec le vrai délai de grâce du service.</summary>
    private Task<int> BalayerAsync(TestGisDbContext ctx) =>
        InvoiceOrphanCleanupService.BalayerAsync(
            ctx, _racine, DateTime.UtcNow - InvoiceOrphanCleanupService.GracePeriod, logger: null, CancellationToken.None);

    // Chaque porteur, avec le libellé exact que son écran écrit (ou la colonne dédiée).
    public static TheoryData<string, string> Porteurs() => new()
    {
        { "cout", "Dépenses et Échéances : vehicle_costs.receipt_url" },
        { "carburant", "Carburant : fuel_entries.notes" },
        { "reparation", "Nouvelle réparation : repairs.notes" },
        { "entretien", "Entretien effectué : maintenance_logs.notes" },
        { "notes-cout", "vehicle_costs.notes" },
        { "acquisition", "acquisition_payments.receipt_url" },
        { "maintenance-record", "maintenance_records.invoice_url" },
        { "document", "vehicle_documents.file_url" },
    };

    private static void Referencer(TestGisDbContext ctx, string porteur, string url)
    {
        switch (porteur)
        {
            case "cout":
                ctx.VehicleCosts.Add(new VehicleCost
                {
                    Id = 1, VehicleId = 38, CompanyId = CompanyId, Type = "fuel", Amount = 104.910m,
                    Date = DateTime.UtcNow, ReceiptUrl = url
                });
                break;
            case "carburant":
                // Libellé exact de carburant.component.ts (saveManualEntry).
                ctx.FuelEntries.Add(new FuelEntry
                {
                    Id = 1, VehicleId = 38, CompanyId = CompanyId, VehiclePlate = "123 TUN 4567",
                    FuelTypeId = 1, Volume = 41.55m, PricePerLiter = 2.525m, TotalAmount = 104.910m,
                    InvoiceDate = DateTime.UtcNow, Notes = "Ticket scanné : " + url
                });
                break;
            case "reparation":
                // Libellé exact de repairs.component.ts (PREFIXE_JUSTIFICATIF).
                ctx.Repairs.Add(new Repair
                {
                    Id = 1, VehicleId = 38, SocieteId = CompanyId, Reference = "REP-001",
                    RepairDate = DateTime.UtcNow, Notes = "Justificatif : " + url
                });
                break;
            case "entretien":
                ctx.MaintenanceLogs.Add(new MaintenanceLog
                {
                    Id = 1, VehicleId = 38, TemplateId = 3, CompanyId = CompanyId,
                    DoneDate = DateTime.UtcNow, DoneKm = 120_000, ActualCost = 342.5m,
                    Notes = "Facture n° A-42 — GARAGE EL AMEN\n" + url
                });
                break;
            case "notes-cout":
                ctx.VehicleCosts.Add(new VehicleCost
                {
                    Id = 2, VehicleId = 38, CompanyId = CompanyId, Type = "maintenance", Amount = 342.5m,
                    Date = DateTime.UtcNow, Notes = "Justificatif : " + url
                });
                break;
            case "acquisition":
                ctx.AcquisitionPayments.Add(new AcquisitionPayment
                {
                    Id = 1, VehicleId = 38, CompanyId = CompanyId, Seq = 1,
                    DueDate = DateOnly.FromDateTime(DateTime.UtcNow), Amount = 1200m, ReceiptUrl = url
                });
                break;
            case "maintenance-record":
                ctx.MaintenanceRecords.Add(new MaintenanceRecord
                {
                    Id = 1, VehicleId = 38, CompanyId = CompanyId, Description = "Vidange",
                    Date = DateTime.UtcNow, InvoiceUrl = url
                });
                break;
            case "document":
                ctx.VehicleDocuments.Add(new VehicleDocument
                {
                    Id = 1, VehicleId = 38, Type = "insurance", Name = "Quittance", FileUrl = url
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(porteur), porteur, "porteur inconnu");
        }
    }

    [Theory]
    [MemberData(nameof(Porteurs))]
    public async Task Un_fichier_reference_par_un_porteur_survit_au_balayage(string porteur, string pourquoi)
    {
        await using var ctx = TestDbContextFactory.Create();
        // Vieux de deux jours : le délai de grâce ne le protège plus, seule la référence le sauve.
        var url = Depose("facture-" + porteur + ".jpg", TimeSpan.FromDays(2));
        Referencer(ctx, porteur, url);
        await ctx.SaveChangesAsync();

        var supprimes = await BalayerAsync(ctx);

        supprimes.Should().Be(0, pourquoi);
        File.Exists(Chemin("facture-" + porteur + ".jpg")).Should().BeTrue(pourquoi);
    }

    [Fact]
    public async Task Un_vrai_orphelin_est_toujours_supprime()
    {
        // Revue abandonnée : le fichier a été stocké, l'utilisateur n'a rien enregistré.
        await using var ctx = TestDbContextFactory.Create();
        Depose("orphelin.jpg", TimeSpan.FromDays(2));

        var supprimes = await BalayerAsync(ctx);

        supprimes.Should().Be(1);
        File.Exists(Chemin("orphelin.jpg")).Should().BeFalse();
    }

    [Fact]
    public async Task Le_delai_de_grace_protege_la_revue_en_cours()
    {
        // 23 h : la facture est à l'écran, en cours de relecture, et n'est référencée
        // nulle part — elle ne doit surtout pas disparaître sous les doigts de l'utilisateur.
        await using var ctx = TestDbContextFactory.Create();
        Depose("en-cours.jpg", TimeSpan.FromHours(23));

        var supprimes = await BalayerAsync(ctx);

        supprimes.Should().Be(0);
        File.Exists(Chemin("en-cours.jpg")).Should().BeTrue();
        InvoiceOrphanCleanupService.GracePeriod.Should().Be(TimeSpan.FromHours(24), "le délai de grâce ne bouge pas");
    }

    [Fact]
    public async Task Le_ticket_de_carburant_survit_et_l_orphelin_du_meme_dossier_part()
    {
        // Le cas réel, en une fois : deux fichiers du même client, même âge, seul le
        // second n'est rattaché à rien.
        await using var ctx = TestDbContextFactory.Create();
        var ticket = Depose("ticket.jpg", TimeSpan.FromDays(3));
        Depose("abandonne.pdf", TimeSpan.FromDays(3));
        Referencer(ctx, "carburant", ticket);
        await ctx.SaveChangesAsync();

        var supprimes = await BalayerAsync(ctx);

        supprimes.Should().Be(1);
        File.Exists(Chemin("ticket.jpg")).Should().BeTrue();
        File.Exists(Chemin("abandonne.pdf")).Should().BeFalse();
    }

    [Theory]
    // Le lien est noyé dans une phrase : c'est ce qu'écrivent Carburant et Réparations,
    // faute de colonne. Il peut être suivi d'une ponctuation ou d'une autre remarque.
    [InlineData("Ticket scanné : /uploads/invoices/7/ticket.jpg")]
    [InlineData("Justificatif : /uploads/invoices/7/ticket.jpg")]
    [InlineData("Facture jointe (/uploads/invoices/7/ticket.jpg), à vérifier")]
    [InlineData("voir /uploads/invoices/7/ticket.jpg.")]
    [InlineData("/uploads/invoices/7/ticket.jpg\nRemarque du chauffeur")]
    [InlineData("/uploads/invoices/7/ticket.jpg")]
    public void Le_lien_est_reconnu_quelle_que_soit_la_phrase_qui_l_entoure(string notes)
        => InvoiceOrphanCleanupService.UrlsDansLeTexte(notes)
            .Should().ContainSingle().Which.Should().Be("/uploads/invoices/7/ticket.jpg");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Plein fait par le chauffeur, sans ticket")]
    [InlineData("/uploads/acquisition-receipts/7/quittance.jpg")]
    public void Un_texte_sans_lien_ne_retient_rien(string? notes)
        => InvoiceOrphanCleanupService.UrlsDansLeTexte(notes).Should().BeEmpty();

    [Fact]
    public void Deux_liens_dans_la_meme_note_sont_tous_les_deux_retenus()
        => InvoiceOrphanCleanupService
            .UrlsDansLeTexte("Ticket : /uploads/invoices/7/a.jpg puis /uploads/invoices/7/b.pdf")
            .Should().BeEquivalentTo("/uploads/invoices/7/a.jpg", "/uploads/invoices/7/b.pdf");

    [Fact]
    public async Task Sans_dossier_le_balayage_ne_plante_pas()
    {
        // Serveur neuf : personne n'a encore scanné quoi que ce soit.
        await using var ctx = TestDbContextFactory.Create();

        (await BalayerAsync(ctx)).Should().Be(0);
    }
}
