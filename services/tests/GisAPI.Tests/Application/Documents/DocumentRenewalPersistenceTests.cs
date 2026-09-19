using FluentAssertions;
using GisAPI.Application.Features.Documents.Commands;
using GisAPI.Application.Features.Documents.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Infrastructure.Persistence.Configurations;
using GisAPI.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.Documents;

/// <summary>
/// Renouvellement de document — campagne de test Calypso GPA du 11/09/2026
/// (DEF-007) : la fenêtre de renouvellement propose numéro de document,
/// fournisseur, notes et justificatif, l'API les acceptait, mais rien n'avait de
/// colonne derrière — la saisie disparaissait sans message et l'historique
/// rendait la description à la place du fournisseur.
///
/// <para>Depuis la migration 047, échéance, fournisseur et notes ont leur
/// colonne ; le numéro et le justificatif vont dans receipt_number /
/// receipt_url. On vérifie l'aller-retour complet : écriture par le
/// renouvellement, restitution par l'historique.</para>
///
/// <para>Ces tests tournent sur le VRAI mapping de vehicle_costs et relisent la
/// base, change tracker vidé. TestGisDbContext déduit VehicleCost par
/// convention (toute propriété y est persistée, même celles que
/// VehicleCostConfiguration ignorait) et une entité suivie se relit depuis la
/// mémoire : les premières versions de ces tests passaient donc aussi sur
/// l'ancien code.</para>
/// </summary>
public class DocumentRenewalPersistenceTests
{
    private const int CompanyId = 7;

    private sealed class ContexteMappingReel : TestGisDbContext
    {
        public ContexteMappingReel(DbContextOptions<TestGisDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfiguration(new VehicleCostConfiguration());
        }
    }

    private static async Task<TestGisDbContext> AvecVehiculeAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            // Comme TestDbContextFactory : on teste la logique, pas les contraintes.
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            cmd.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<TestGisDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options;
        var ctx = new ContexteMappingReel(options);
        ctx.Database.EnsureCreated();

        ctx.Vehicles.Add(new Vehicle
        {
            Id = 84,
            CompanyId = CompanyId,
            Name = "QA-LEA",
            Type = "Utilitaire",
            Plate = "QA-002-LEA",
            Status = "available"
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static RenewDocumentCommandHandler Handler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object,
            NullLogger<RenewDocumentCommandHandler>.Instance);

    private static GetRenewalHistoryQueryHandler HistoriqueHandler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

    private static RenewDocumentCommand Renouvellement(
        decimal amount,
        string? documentNumber = "QA-POL-001",
        string? provider = "QA-Assureur",
        string? notes = "QA renouvellement",
        string? documentUrl = "/uploads/qa-police.pdf") => new(
        VehicleId: 84,
        DocumentType: "insurance",
        Amount: amount,
        PaymentDate: new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
        NewExpiryDate: new DateTime(2027, 9, 15, 0, 0, 0, DateTimeKind.Utc),
        DocumentNumber: documentNumber,
        Provider: provider,
        Notes: notes,
        DocumentUrl: documentUrl);

    /// Dépense relue depuis la base, pas depuis l'instance suivie par EF.
    private static VehicleCost Relue(TestGisDbContext ctx, int costId)
    {
        ctx.ChangeTracker.Clear();
        return ctx.VehicleCosts.AsNoTracking().Single(c => c.Id == costId);
    }

    [Fact]
    public async Task Le_numero_de_police_et_le_justificatif_sont_enregistres()
    {
        using var ctx = await AvecVehiculeAsync();

        var costId = await Handler(ctx).Handle(Renouvellement(321.50m), CancellationToken.None);

        var cost = Relue(ctx, costId);
        cost.ReceiptNumber.Should().Be("QA-POL-001");
        cost.ReceiptUrl.Should().Be("/uploads/qa-police.pdf");
        cost.Description.Should().Be("Renouvellement Assurance - QA-Assureur");
        ctx.Vehicles.AsNoTracking().Single().InsuranceExpiry
            .Should().Be(new DateTime(2027, 9, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Le_fournisseur_les_notes_et_l_echeance_sont_enregistres()
    {
        using var ctx = await AvecVehiculeAsync();

        var costId = await Handler(ctx).Handle(Renouvellement(321.50m), CancellationToken.None);

        var cost = Relue(ctx, costId);
        cost.Provider.Should().Be("QA-Assureur");
        cost.Notes.Should().Be("QA renouvellement");
        cost.ExpiryDate.Should().Be(new DateTime(2027, 9, 15, 0, 0, 0, DateTimeKind.Utc),
            "l'échéance a sa colonne expiry_date : VehicleCostConfiguration l'ignorait");
    }

    [Fact]
    public async Task L_historique_restitue_le_numero_le_fournisseur_et_les_notes()
    {
        using var ctx = await AvecVehiculeAsync();
        var costId = await Handler(ctx).Handle(Renouvellement(321.50m), CancellationToken.None);
        ctx.ChangeTracker.Clear();

        var historique = await HistoriqueHandler(ctx)
            .Handle(new GetRenewalHistoryQuery(84), CancellationToken.None);

        var ligne = historique.Single(h => h.Id == costId);
        ligne.DocumentNumber.Should().Be("QA-POL-001",
            "le numéro est relu de receipt_number : DocumentNumber n'avait aucune colonne");
        ligne.Provider.Should().Be("QA-Assureur",
            "le fournisseur a sa colonne : la description est un libellé libre, pas un fournisseur");
        ligne.Notes.Should().Be("QA renouvellement");
        ligne.DocumentUrl.Should().Be("/uploads/qa-police.pdf");
        ligne.ExpiryDate.Should().Be(new DateTime(2027, 9, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Un_renouvellement_sans_montant_met_a_jour_l_echeance_sans_creer_de_depense()
    {
        using var ctx = await AvecVehiculeAsync();

        var costId = await Handler(ctx).Handle(Renouvellement(0m), CancellationToken.None);

        costId.Should().Be(0);
        ctx.ChangeTracker.Clear();
        ctx.VehicleCosts.Should().BeEmpty();
        ctx.Vehicles.Single().InsuranceExpiry.Should().Be(new DateTime(2027, 9, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    // ── Saisies bornées aux tailles des colonnes ─────────────────────────────

    [Theory]
    [InlineData("documentNumber", 101, "Numéro de document : 100")]
    [InlineData("provider", 201, "Fournisseur : 200")]
    [InlineData("notes", 1001, "Notes : 1000")]
    [InlineData("documentUrl", 501, "Lien du justificatif : 500")]
    public async Task Une_saisie_plus_longue_que_sa_colonne_est_refusee_sans_rien_enregistrer(
        string champ, int longueur, string messageAttendu)
    {
        using var ctx = await AvecVehiculeAsync();
        var tropLong = new string('x', longueur);
        var commande = champ switch
        {
            "documentNumber" => Renouvellement(321.50m, documentNumber: tropLong),
            "provider" => Renouvellement(321.50m, provider: tropLong),
            "notes" => Renouvellement(321.50m, notes: tropLong),
            _ => Renouvellement(321.50m, documentUrl: tropLong)
        };

        var act = () => Handler(ctx).Handle(commande, CancellationToken.None);

        // DomainException exacte = 400 avec le message (ExceptionHandlingMiddleware),
        // au lieu de l'erreur PostgreSQL « value too long » rendue en 500.
        (await act.Should().ThrowExactlyAsync<DomainException>())
            .Which.Message.Should().StartWith(messageAttendu);
        ctx.ChangeTracker.Clear();
        ctx.VehicleCosts.Should().BeEmpty();
        ctx.Vehicles.Single().InsuranceExpiry.Should().BeNull("le renouvellement refusé ne touche pas à l'échéance");
    }

    [Fact]
    public async Task Une_saisie_a_la_taille_exacte_de_sa_colonne_est_acceptee()
    {
        using var ctx = await AvecVehiculeAsync();

        var costId = await Handler(ctx).Handle(Renouvellement(321.50m,
            documentNumber: new string('N', 100),
            provider: new string('F', 200),
            notes: new string('n', 1000),
            documentUrl: new string('u', 500)), CancellationToken.None);

        var cost = Relue(ctx, costId);
        cost.ReceiptNumber.Should().HaveLength(100);
        cost.Provider.Should().HaveLength(200);
        cost.Notes.Should().HaveLength(1000);
        cost.ReceiptUrl.Should().HaveLength(500);
    }

    // ── Renouvellements enregistrés avant la migration 047 ──────────────────

    [Theory]
    [InlineData("insurance", "Renouvellement Assurance - AXA Flotte Entreprise", "AXA Flotte Entreprise")]
    [InlineData("technical_inspection", "Renouvellement Visite technique - Centre Ariana", "Centre Ariana")]
    [InlineData("tax", "Renouvellement Vignette", null)]
    [InlineData("insurance", "Assurance annuelle STAR", "Assurance annuelle STAR")]
    public async Task Sans_colonne_fournisseur_l_historique_relit_la_description(
        string type, string description, string? fournisseurAttendu)
    {
        using var ctx = await AvecVehiculeAsync();
        // Ligne antérieure à la migration : provider NULL, tout est dans la description.
        ctx.VehicleCosts.Add(new VehicleCost
        {
            Id = 238,
            CompanyId = CompanyId,
            VehicleId = 84,
            Type = type,
            Description = description,
            Amount = 321.50m,
            Date = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var historique = await HistoriqueHandler(ctx)
            .Handle(new GetRenewalHistoryQuery(84), CancellationToken.None);

        historique.Single(h => h.Id == 238).Provider.Should().Be(fournisseurAttendu);
    }

    /// <summary>
    /// Renouvellement SANS montant (19/09/2026) : aucune dépense n'est créée —
    /// règle du 26/08/2026, une ligne à zéro fausserait les totaux — mais plus
    /// personne ne portait alors le lien de la quittance scannée. Le fichier
    /// n'était référencé nulle part et le balayage des factures orphelines
    /// l'effaçait le lendemain, sans message. Il est désormais rangé dans les
    /// documents du véhicule.
    /// </summary>
    [Fact]
    public async Task Sans_montant_la_quittance_est_rangee_dans_les_documents_du_vehicule()
    {
        using var ctx = await AvecVehiculeAsync();

        var costId = await Handler(ctx).Handle(Renouvellement(0m), CancellationToken.None);

        costId.Should().Be(0, "aucune dépense n'est créée sans montant");
        ctx.ChangeTracker.Clear();
        ctx.VehicleCosts.AsNoTracking().Should().BeEmpty();

        var doc = ctx.VehicleDocuments.AsNoTracking().Single();
        doc.VehicleId.Should().Be(84);
        doc.Type.Should().Be("insurance");
        doc.FileUrl.Should().Be("/uploads/qa-police.pdf");
        doc.Name.Should().Be("Renouvellement Assurance - QA-Assureur");
        doc.ExpiryDate.Should().Be(new DateTime(2027, 9, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    /// Avec un montant, la quittance reste sur la dépense : pas de doublon.
    [Fact]
    public async Task Avec_un_montant_la_quittance_reste_sur_la_depense_et_ne_double_pas()
    {
        using var ctx = await AvecVehiculeAsync();

        var costId = await Handler(ctx).Handle(Renouvellement(321.50m), CancellationToken.None);

        Relue(ctx, costId).ReceiptUrl.Should().Be("/uploads/qa-police.pdf");
        ctx.VehicleDocuments.AsNoTracking().Should().BeEmpty();
    }

    /// Deux renouvellements sans montant sur la même quittance : une seule ligne.
    [Fact]
    public async Task Deux_renouvellements_sans_montant_ne_rangent_la_quittance_qu_une_fois()
    {
        using var ctx = await AvecVehiculeAsync();

        await Handler(ctx).Handle(Renouvellement(0m), CancellationToken.None);
        ctx.ChangeTracker.Clear();
        await Handler(ctx).Handle(Renouvellement(0m), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        ctx.VehicleDocuments.AsNoTracking().Should().HaveCount(1);
    }

    /// Sans montant ET sans quittance : rien n'est rangé.
    [Fact]
    public async Task Sans_montant_ni_quittance_aucun_document_n_est_cree()
    {
        using var ctx = await AvecVehiculeAsync();

        await Handler(ctx).Handle(Renouvellement(0m, documentUrl: null), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        ctx.VehicleDocuments.AsNoTracking().Should().BeEmpty();
    }
}
