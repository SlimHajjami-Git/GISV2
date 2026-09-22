using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AiCredits;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.AiCredits;

/// <summary>
/// Récit d'accident rédigé par l'IA (22/09/2026, « le quota inclut l'utilisation de l'IA ») :
/// chaque appel réussi est ENREGISTRÉ au crédit IA de la société (fonction
/// accident_narrative, sans utilisateur : c'est le système qui le déclenche), mais le récit
/// n'est JAMAIS bloqué par le crédit — fonction de sécurité. Société inconnue : rien n'est
/// enregistré.
/// </summary>
public class AccidentNarrativeCreditTests
{
    private const int CompanyId = 7;

    /// <summary>GisDbContext en mémoire, sans les colonnes propres à PostgreSQL (même montage
    /// que CreditsDeduitsPartoutTests) : le service de récit exige le contexte concret. Société
    /// courante absente, comme dans la tâche de fond de détection des accidents.</summary>
    private sealed class ContexteEnMemoire : GisDbContext
    {
        public ContexteEnMemoire(DbContextOptions<GisDbContext> options) : base(options, SansSociete()) { }

        private static ICurrentTenantService SansSociete()
        {
            var tenant = new Mock<ICurrentTenantService>();
            tenant.Setup(t => t.CompanyId).Returns((int?)null);
            tenant.Setup(t => t.UserRoles).Returns(Array.Empty<string>());
            return tenant.Object;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToList())
                foreach (var property in entity.GetDeclaredProperties().ToList())
                    if (!Simple(property.ClrType) && !property.IsKey() && !property.IsForeignKey())
                        modelBuilder.Entity(entity.ClrType).Ignore(property.Name);
        }

        private static bool Simple(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
                || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[])
                || t == typeof(DateOnly) || t == typeof(TimeOnly);
        }
    }

    /// <summary>Relevés fournis par le test : la lecture SQL PostgreSQL n'est pas l'objet ici.</summary>
    private sealed class RecitAvecReleves : AccidentNarrativeService
    {
        public RecitAvecReleves(ILlmService llm) : base(llm, NullLogger<AccidentNarrativeService>.Instance) { }

        internal override Task<List<AccidentFrame>> LoadFramesAsync(GisDbContext context, AccidentCandidate c, CancellationToken ct) =>
            Task.FromResult(Enumerable.Range(-5, 12)
                .Select(i => new AccidentFrame { RecordedAt = c.RecordedAt.AddSeconds(i * 10), SpeedKph = i < 0 ? 60 : 0, IgnitionOn = true })
                .ToList());
    }

    /// <summary>Journal du crédit inaccessible (table ai_usage_logs absente : pod déployé avant la
    /// migration 052, ou base coupée) : l'INSERT d'une ligne du journal échoue, toute autre
    /// écriture passe.</summary>
    private sealed class JournalIaInaccessible : SaveChangesInterceptor
    {
        public int Refus { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AiUsageLog>().Any(e => e.State == EntityState.Added))
            {
                Refus++;
                throw new DbUpdateException("42P01: relation \"ai_usage_logs\" does not exist");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static async Task<ContexteEnMemoire> SocieteAsync(int? tokens, int dejaConsomme = 0, IInterceptor? intercepteur = null)
    {
        var options = new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false));
        if (intercepteur is not null)
            options.AddInterceptors(intercepteur);
        var ctx = new ContexteEnMemoire(options.Options);
        ctx.Societes.Add(new Societe
        {
            Id = CompanyId, Name = "Hertz", SubscriptionStatus = "active", IsActive = true,
            InvoiceScanMonthlyTokens = tokens
        });
        if (dejaConsomme > 0)
            ctx.AiUsageLogs.Add(new AiUsageLog { CompanyId = CompanyId, UserId = 45, Feature = AiFeatures.AssistantChat, TokensUsed = dejaConsomme, CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static AccidentCandidate Choc() => new()
    {
        DeviceId = 31, RecordedAt = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc),
        Ax = 110, Ay = 20, Az = 5, Mag = 150, KphBef = 60, KphJustBef = 60, KphAft = 0
    };

    private const string RecitValide =
        "{\"synthesisText\": \"Le véhicule circulait à 60 km/h puis s'est immobilisé.\", " +
        "\"reasons\": [{\"title\": \"Arrêt brutal\", \"text\": \"Passage de 60 km/h à l'arrêt.\"}]}";

    private static Mock<ILlmService> Llm(string reponse, int tokens)
    {
        var llm = new Mock<ILlmService>(MockBehavior.Strict);
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LlmResponse(reponse, tokens));
        return llm;
    }

    [Theory]
    [InlineData(0, 0)]          // IA désactivée pour la société
    [InlineData(10_000, 10_000)] // crédit du mois épuisé
    [InlineData(null, 0)]       // crédit disponible
    public async Task Le_recit_n_est_jamais_bloque_par_le_credit_et_sa_consommation_est_enregistree(int? tokens, int deja)
    {
        await using var ctx = await SocieteAsync(tokens, deja);
        var llm = Llm(RecitValide, 1_850);

        var recit = await new RecitAvecReleves(llm.Object).TryGenerateAsync(ctx, Choc(), "GA-214-RK", null, CompanyId, CancellationToken.None);

        recit.Should().NotBeNull("un récit d'accident n'est jamais refusé faute de crédit");
        llm.Verify(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        var ligne = ctx.AiUsageLogs.Single(l => l.Feature == AiFeatures.AccidentNarrative);
        ligne.CompanyId.Should().Be(CompanyId);
        ligne.UserId.Should().BeNull("déclenché par le système, pas par un utilisateur");
        ligne.TokensUsed.Should().Be(1_850);
    }

    [Fact]
    public async Task Un_recit_rejete_par_le_garde_fou_a_quand_meme_consomme_ses_jetons()
    {
        await using var ctx = await SocieteAsync(null);

        var recit = await new RecitAvecReleves(Llm("pas du JSON", 900).Object)
            .TryGenerateAsync(ctx, Choc(), "GA-214-RK", null, CompanyId, CancellationToken.None);

        recit.Should().BeNull("le récit déterministe est conservé");
        ctx.AiUsageLogs.Single().TokensUsed.Should().Be(900, "l'appel a été payé à Groq");
    }

    [Fact]
    public async Task Societe_inconnue_le_recit_est_redige_mais_rien_n_est_enregistre()
    {
        await using var ctx = await SocieteAsync(null);

        var recit = await new RecitAvecReleves(Llm(RecitValide, 1_850).Object)
            .TryGenerateAsync(ctx, Choc(), "GA-214-RK", null, companyId: null, CancellationToken.None);

        recit.Should().NotBeNull();
        ctx.AiUsageLogs.Should().BeEmpty("jamais de ligne sous la société 0");
    }

    [Fact]
    public async Task Un_appel_en_echec_n_enregistre_rien()
    {
        await using var ctx = await SocieteAsync(null);
        var llm = new Mock<ILlmService>(MockBehavior.Strict);
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new HttpRequestException("Groq indisponible"));

        var recit = await new RecitAvecReleves(llm.Object)
            .TryGenerateAsync(ctx, Choc(), "GA-214-RK", null, CompanyId, CancellationToken.None);

        recit.Should().BeNull();
        ctx.AiUsageLogs.Should().BeEmpty();
    }

    /// <summary>
    /// Relecture du 22/09/2026 : le cycle de détection (AccidentDetectionService.ScanAsync) passe
    /// UN seul contexte à tous ses candidats, et le récit y ajoute sa ligne de consommation. Si
    /// l'écriture de cette ligne échoue, la panne est journalisée et ignorée — mais la ligne
    /// restait « à insérer » dans ce contexte : la sauvegarde du récit sur l'accident échouait
    /// (récit perdu), puis la création de l'accident suivant du cycle (ni créé ni notifié).
    /// </summary>
    [Fact]
    public async Task Journal_du_credit_en_panne_le_recit_et_l_accident_suivant_du_cycle_sont_quand_meme_enregistres()
    {
        var journal = new JournalIaInaccessible();
        await using var ctx = await SocieteAsync(null, intercepteur: journal);
        ctx.AccidentEvents.Add(Accident(1));
        await ctx.SaveChangesAsync();

        var recit = await new RecitAvecReleves(Llm(RecitValide, 1_850).Object)
            .TryGenerateAsync(ctx, Choc(), "GA-214-RK", null, CompanyId, CancellationToken.None);

        recit.Should().NotBeNull("le récit n'est jamais bloqué, pas même par une panne du journal");
        journal.Refus.Should().Be(1, "l'écriture de la consommation a bien été tentée puis refusée");

        // TryEnrichNarrativeAsync : le récit est posé sur l'accident, puis sauvegardé.
        var ev = ctx.AccidentEvents.IgnoreQueryFilters().Single(e => e.Id == 1);
        ev.SynthesisText = recit!.SynthesisText;
        await ctx.SaveChangesAsync();

        // Candidat suivant du même cycle : CreateAccidentEventAsync, dans le même contexte.
        ctx.AccidentEvents.Add(Accident(2));
        await ctx.SaveChangesAsync();

        journal.Refus.Should().Be(1, "la ligne refusée n'est jamais rejouée");
        ctx.ChangeTracker.Clear();
        ctx.AccidentEvents.IgnoreQueryFilters().Single(e => e.Id == 1).SynthesisText.Should().Be(recit.SynthesisText);
        ctx.AccidentEvents.IgnoreQueryFilters().Select(e => e.Id).Should().BeEquivalentTo(new[] { 1, 2 });
        ctx.AiUsageLogs.Should().BeEmpty();
    }

    private static AccidentEvent Accident(int id) => new()
    {
        Id = id, CompanyId = CompanyId, VehicleId = 49, GpsDeviceId = 31, DeviceUid = "861234567890123",
        IncidentAt = Choc().RecordedAt.AddMinutes(id * 30), ReferenceCode = $"ACC-{id}",
        SynthesisText = "Récit déterministe", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
}
