using FluentAssertions;
using GisAPI.Application.Features.Admin.Companies.Commands.ResetCompanyData;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.Admin;

/// <summary>
/// Le handler est testé sur un magasin factice qui enregistre chaque ordre : ce qui compte ici,
/// c'est la discipline (admin système, nom ressaisi, garde-fous avant, transaction, témoins,
/// aperçu sans transaction, trace d'audit), pas le SQL — celui-ci est couvert par
/// CompanyDataResetPlannerTests sur le schéma réel.
/// </summary>
public class ResetCompanyDataCommandHandlerTests
{
    private sealed class FakeStore : ICompanyDataStore
    {
        public readonly List<string> Executed = new();
        public readonly List<string> Events = new();
        public readonly List<(string Table, string Where)> Counted = new();
        public Dictionary<string, int> RowsPerTable { get; } = new();
        public Func<string, string, long>? Count;                       // (table, where) → count
        public Dictionary<string, List<string>> Files { get; } = new();  // "table.column" → valeurs
        public Exception? ThrowOnExecute;

        public Task<CompanyDataCatalog> LoadCatalogAsync(CancellationToken ct) => Task.FromResult(TnCatalogFixture.Load());
        public Task BeginTransactionAsync(CancellationToken ct) { Events.Add("BEGIN"); return Task.CompletedTask; }
        public Task CommitAsync(CancellationToken ct) { Events.Add("COMMIT"); return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken ct) { Events.Add("ROLLBACK"); return Task.CompletedTask; }

        public Task<int> ExecuteAsync(string sql, int companyId, CancellationToken ct)
        {
            if (ThrowOnExecute is not null) throw ThrowOnExecute;
            Executed.Add(sql);
            var table = sql.Split('"')[1];
            return Task.FromResult(RowsPerTable.TryGetValue(table, out var n) ? n : 0);
        }

        public Task<long> CountAsync(string quotedTable, string where, int companyId, CancellationToken ct)
        {
            var table = quotedTable.Trim('"');
            Counted.Add((table, where));
            return Task.FromResult(Count?.Invoke(table, where) ?? 0L);
        }

        public Task<IReadOnlyList<string>> SelectStringsAsync(string quotedTable, string quotedColumn, string where, int companyId, CancellationToken ct)
        {
            var key = quotedTable.Trim('"') + "." + quotedColumn.Trim('"');
            return Task.FromResult<IReadOnlyList<string>>(Files.TryGetValue(key, out var v) ? v : new List<string>());
        }
    }

    /// <summary>Témoins stables (2 utilisateurs, 2 rôles, la société) ; un garde-fou (« AND NOT ») vaut 0.</summary>
    private static long StableCounts(string table, string where) => where.Contains("AND NOT (") ? 0
        : table switch { "users" => 2, "roles" => 2, "societes" => 1, "vehicles" when where.Contains("<>") => 380, "vehicle_costs" when where.Contains("<>") => 5000, _ => 0 };

    private static (ResetCompanyDataCommandHandler handler, FakeStore store, TestGisDbContext ctx) Build(int adminUserId = 1, bool systemAdmin = true)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(new Societe { Id = 14, Name = "Belive GPA", SubscriptionStatus = "active", IsActive = true });
        ctx.Societes.Add(new Societe { Id = 10, Name = "SICOAC", SubscriptionStatus = "active", IsActive = true });
        ctx.SaveChanges();

        var store = new FakeStore { Count = StableCounts };
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 0, userId: adminUserId);
        tenant.Setup(t => t.IsSystemAdmin).Returns(systemAdmin);
        var handler = new ResetCompanyDataCommandHandler(ctx, store, tenant.Object, NullLogger<ResetCompanyDataCommandHandler>.Instance);
        return (handler, store, ctx);
    }

    [Fact]
    public async Task Seul_l_administrateur_systeme_peut_lancer_la_remise_a_zero()
    {
        var (handler, store, _) = Build(systemAdmin: false);
        var act = () => handler.Handle(new ResetCompanyDataCommand(14, "Belive GPA", null), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenAccessException>();
        store.Events.Should().BeEmpty();
        store.Executed.Should().BeEmpty();
    }

    [Fact]
    public async Task Le_nom_doit_etre_ressaisi_exactement_sinon_rien_n_est_execute()
    {
        var (handler, store, _) = Build();

        var act = () => handler.Handle(new ResetCompanyDataCommand(14, "Belive gpa", null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*nom saisi*");
        store.Executed.Should().BeEmpty();
        store.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Une_societe_inconnue_est_refusee()
    {
        var (handler, _, _) = Build();
        var act = () => handler.Handle(new ResetCompanyDataCommand(999, "X", null), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Le_plan_complet_s_execute_dans_une_transaction_validee_et_laisse_une_trace()
    {
        var (handler, store, ctx) = Build(adminUserId: 7);
        store.RowsPerTable["vehicles"] = 4;
        store.RowsPerTable["vehicle_costs"] = 36;
        store.RowsPerTable["acquisition_payments"] = 112;

        var result = await handler.Handle(new ResetCompanyDataCommand(14, "Belive GPA", null), CancellationToken.None);

        store.Events.Should().Equal("BEGIN", "COMMIT");
        var plan = CompanyDataResetPlanner.Plan(TnCatalogFixture.Load());
        store.Executed.Should().HaveCount(plan.Steps.Count);
        store.Executed.Select(s => s.Split('"')[1]).Should().Equal(plan.Steps.Select(s => s.Table), "l'ordre du plan est respecté");
        store.Executed.Should().AllSatisfy(sql => sql.Should().StartWith("DELETE FROM \"").And.Contain("{0}"));
        store.Executed.Select(s => s.Split('"')[1]).Should().NotContain(new[] { "users", "roles", "societes", "gps_devices" });
        store.Counted.Should().Contain(c => c.Where.Contains("AND NOT ("), "les garde-fous de cascade sont contrôlés avant de supprimer");

        result.DryRun.Should().BeFalse();
        result.TotalRows.Should().Be(152);
        result.Deleted.Select(d => d.Table).Should().BeEquivalentTo(new[] { "vehicles", "vehicle_costs", "acquisition_payments" });
        result.Kept.Should().Contain("users");

        var audit = ctx.AuditLogs.Single();
        audit.Action.Should().Be("company_data_reset");
        audit.UserId.Should().Be(7);
        audit.CompanyId.Should().BeNull("la trace doit survivre à une prochaine remise à zéro");
        audit.EntityId.Should().Be(14);
        audit.NewValues.Should().ContainKey("vehicles");
    }

    [Fact]
    public async Task L_apercu_ne_fait_que_compter_sans_transaction_ni_nom_ni_trace()
    {
        var (handler, store, ctx) = Build();
        store.Count = (table, where) => table == "vehicles" && !where.Contains("<>") ? 4 : StableCounts(table, where);

        var result = await handler.Handle(new ResetCompanyDataCommand(14, "", null, DryRun: true), CancellationToken.None);

        store.Events.Should().BeEmpty("aucune transaction, aucun verrou");
        store.Executed.Should().BeEmpty("aucun DELETE, même annulé");
        store.Counted.Should().Contain(c => c.Table == "vehicle_costs" && !c.Where.Contains("<>"));
        result.DryRun.Should().BeTrue();
        result.TotalRows.Should().Be(4);
        result.Deleted.Should().ContainSingle(d => d.Table == "vehicles" && d.Rows == 4);
        ctx.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Une_ligne_d_une_autre_societe_sous_un_vehicule_de_la_societe_interdit_la_remise_a_zero()
    {
        // Véhicule transféré : ses anciens trajets portent l'autre société, la cascade les effacerait.
        var (handler, store, ctx) = Build();
        store.Count = (table, where) => table == "trips" && where.Contains("AND NOT (") ? 12 : StableCounts(table, where);

        var act = () => handler.Handle(new ResetCompanyDataCommand(14, "Belive GPA", null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*trips*autre société*12 ligne(s)*");
        store.Executed.Should().BeEmpty();
        store.Events.Should().Equal("BEGIN", "ROLLBACK");
        ctx.AuditLogs.Should().BeEmpty();

        // L'aperçu le dit aussi, avant même de tenter.
        var preview = () => handler.Handle(new ResetCompanyDataCommand(14, "", null, DryRun: true), CancellationToken.None);
        await preview.Should().ThrowAsync<DomainException>().WithMessage("*trips*");
    }

    [Fact]
    public async Task Un_temoin_qui_bouge_annule_tout()
    {
        var (handler, store, ctx) = Build();
        var usersCalls = 0;
        store.Count = (table, where) => table == "users" ? (++usersCalls == 1 ? 2 : 1) : StableCounts(table, where);

        var act = () => handler.Handle(new ResetCompanyDataCommand(14, "Belive GPA", null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Garde-fou*utilisateurs 2→1*");
        store.Events.Should().Equal("BEGIN", "ROLLBACK");
        ctx.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_vehicule_d_une_autre_societe_qui_disparait_annule_tout()
    {
        var (handler, store, _) = Build();
        var otherCalls = 0;
        store.Count = (table, where) => table == "vehicles" && where.Contains("<>") ? (++otherCalls == 1 ? 380 : 379) : StableCounts(table, where);

        var act = () => handler.Handle(new ResetCompanyDataCommand(14, "Belive GPA", null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*autres sociétés 380→379*");
        store.Events.Should().Equal("BEGIN", "ROLLBACK");
    }

    [Fact]
    public async Task Un_refus_de_la_base_est_rendu_en_clair_et_annule()
    {
        var (handler, store, ctx) = Build();
        store.ThrowOnExecute = new InvalidOperationException("23503: la clé (DepartmentId)=(3) est encore référencée");

        var act = () => handler.Handle(new ResetCompanyDataCommand(14, "Belive GPA", null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*rien n'a été modifié*23503*");
        store.Events.Should().Equal("BEGIN", "ROLLBACK");
        ctx.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Les_fichiers_references_sont_supprimes_sous_la_racine_uploads_seulement()
    {
        var root = Path.Combine(Path.GetTempPath(), "gisv2-reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "accident-reports", "37"));
        var pdf = Path.Combine(root, "accident-reports", "37", "rapport.pdf");
        await File.WriteAllTextAsync(pdf, "%PDF");
        var outside = Path.Combine(Path.GetTempPath(), "gisv2-reset-outside-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "x");
        try
        {
            var (handler, store, _) = Build();
            store.Files["accident_events.pdf_report_url"] = new List<string> { "/uploads/accident-reports/37/rapport.pdf" };
            store.Files["maintenance_logs.photos"] = new List<string> { "[\"/uploads/../../" + Path.GetFileName(outside) + "\", \"https://cdn/x.jpg\"]" };
            store.Files["notifications.ActionUrl"] = new List<string> { "/vehicles/12" };

            var result = await handler.Handle(new ResetCompanyDataCommand(14, "Belive GPA", root), CancellationToken.None);

            result.FilesDeleted.Should().Be(1);
            File.Exists(pdf).Should().BeFalse();
            File.Exists(outside).Should().BeTrue("un chemin qui sort de la racine est ignoré");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            File.Delete(outside);
        }
    }

    [Fact]
    public void Les_chemins_uploads_sont_extraits_d_une_valeur_simple_ou_d_un_tableau_json()
    {
        ResetCompanyDataCommandHandler.ExtractUploadPaths("/uploads/a.pdf").Should().Equal("/uploads/a.pdf");
        ResetCompanyDataCommandHandler.ExtractUploadPaths("https://ailleurs/x.pdf").Should().BeEmpty();
        ResetCompanyDataCommandHandler.ExtractUploadPaths("/vehicles/12").Should().BeEmpty("une route applicative n'est pas un fichier");
        ResetCompanyDataCommandHandler.ExtractUploadPaths("[\"/uploads/p1.jpg\",\"/uploads/p2.jpg\",\"data:...\"]")
            .Should().Equal("/uploads/p1.jpg", "/uploads/p2.jpg");
        ResetCompanyDataCommandHandler.ExtractUploadPaths("[pas du json").Should().BeEmpty();
        ResetCompanyDataCommandHandler.ExtractUploadPaths(null).Should().BeEmpty();
    }
}
