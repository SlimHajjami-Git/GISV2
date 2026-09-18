using FluentAssertions;
using GisAPI.Application.Features.Admin.Dashboard;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Application.Features.Users.Commands.DeleteUser;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace GisAPI.Tests.Application.Admin;

/// <summary>
/// M9-AFFICHAGES (journal d'activité admin) : l'auteur affiché valait User.Name, sinon
/// EntityName, sinon « Système ». Depuis que la suppression d'un compte détache ses lignes
/// d'audit (UserId à NULL), ses actions passaient pour des actions du système
/// (AuditTrailMiddleware n'écrit pas d'EntityName), ou pour des actions de l'objet visé
/// (plaque d'une correction de kilométrage, adresse d'une connexion).
///
/// Ce qui est vérifié : action d'un compte supprimé → « Utilisateur supprimé » ; refus de
/// connexion (écrit sans UserId) → l'adresse inscrite ; action inconnue sans auteur →
/// « Système » ; compte existant → son nom.
/// </summary>
public class ActivityLogAuthorTests
{
    private const int CompanyId = 7;
    private const int CallerId = 1;
    private const int DeletedId = 42;

    private static async Task<List<ActivityLogDto>> QueryAsync(TestGisDbContext ctx)
    {
        ctx.ChangeTracker.Clear();
        return await new GetActivityLogsQueryHandler(ctx).Handle(new GetActivityLogsQuery(100), CancellationToken.None);
    }

    private static TestGisDbContext Seed()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.Add(new Role { Id = 1, Name = "Employé", SocieteId = CompanyId });
        var caller = TestDataBuilder.CreateUser(id: CallerId, companyId: CompanyId, email: "admin@test.com");
        caller.FirstName = "Karim";
        caller.LastName = "Admin";
        ctx.Users.AddRange(caller,
            TestDataBuilder.CreateUser(id: DeletedId, companyId: CompanyId, email: "parti@test.com"));
        return ctx;
    }

    [Fact]
    public async Task Les_actions_d_un_compte_supprime_ne_sont_attribuees_ni_au_systeme_ni_a_l_objet_vise()
    {
        await using var ctx = Seed();
        var t0 = new DateTime(2026, 9, 17, 8, 0, 0, DateTimeKind.Utc);
        ctx.AuditLogs.AddRange(
            // LoginCommandHandler : EntityName = adresse du compte.
            new AuditLog { UserId = DeletedId, CompanyId = CompanyId, Action = "login", EntityType = "User", EntityId = DeletedId, EntityName = "parti@test.com", Timestamp = t0 },
            // AuditTrailMiddleware : jamais d'EntityName.
            new AuditLog { UserId = DeletedId, CompanyId = CompanyId, Action = "suppression_costs", EntityType = "costs", EntityId = 900, Description = "DELETE /api/costs/900", Timestamp = t0.AddMinutes(1) },
            // CorrectVehicleMileageCommand : EntityName = plaque du véhicule.
            new AuditLog { UserId = DeletedId, CompanyId = CompanyId, Action = "vehicle_mileage_corrected", EntityType = "Vehicle", EntityId = 40, EntityName = "123 TU 4567", Timestamp = t0.AddMinutes(2) },
            new AuditLog { UserId = CallerId, CompanyId = CompanyId, Action = "creation_vehicles", EntityType = "vehicles", Timestamp = t0.AddMinutes(3) });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: CallerId).Object;
        await new DeleteUserCommandHandler(ctx, tenant).Handle(new DeleteUserCommand(DeletedId), CancellationToken.None);

        var logs = await QueryAsync(ctx);

        logs.Should().HaveCount(4);
        foreach (var action in new[] { "login", "suppression_costs", "vehicle_mileage_corrected" })
        {
            var log = logs.Single(l => l.Action == action);
            log.UserName.Should().Be(GetActivityLogsQueryHandler.DeletedUserLabel, $"« {action} » a été faite par le compte supprimé");
            log.UserId.Should().Be(0);
        }
        logs.Single(l => l.Action == "creation_vehicles").UserName.Should().Be("Karim Admin");
    }

    [Fact]
    public async Task Un_refus_de_connexion_garde_l_adresse_inscrite_et_une_action_sans_auteur_connu_reste_systeme()
    {
        await using var ctx = Seed();
        var t0 = new DateTime(2026, 9, 17, 9, 0, 0, DateTimeKind.Utc);
        ctx.AuditLogs.AddRange(
            new AuditLog { UserId = null, CompanyId = null, Action = LoginCommandHandler.FailedLoginAction, EntityType = "User", EntityName = "ka***@gmail.com", Description = "Échec de connexion : adresse inconnue", Timestamp = t0 },
            new AuditLog { UserId = null, CompanyId = CompanyId, Action = LoginCommandHandler.FailedLoginAction, EntityType = "User", EntityId = CallerId, EntityName = "admin@test.com", Description = "Échec de connexion : mot de passe incorrect", Timestamp = t0.AddMinutes(1) },
            // Ligne ancienne ou écrite par un traitement sans utilisateur : aucune action utilisateur connue.
            new AuditLog { UserId = null, CompanyId = CompanyId, Action = "sauvegarde_automatique", EntityType = "database", Timestamp = t0.AddMinutes(2) });
        await ctx.SaveChangesAsync();

        var logs = await QueryAsync(ctx);

        logs.Single(l => l.Details.EndsWith("adresse inconnue")).UserName.Should().Be("ka***@gmail.com");
        logs.Single(l => l.Details.EndsWith("mot de passe incorrect")).UserName.Should().Be("admin@test.com");
        logs.Single(l => l.Action == "sauvegarde_automatique").UserName.Should().Be(GetActivityLogsQueryHandler.SystemLabel);
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("session")]
    [InlineData("impersonate")]
    [InlineData("company_data_reset")]
    [InlineData("creation_vehicle-maintenance")]
    [InlineData("modification_users")]
    public void Toute_action_ecrite_avec_son_auteur_devient_utilisateur_supprime_sans_UserId(string action)
    {
        var log = new AuditLog { UserId = null, Action = action, EntityType = "User", EntityName = "objet-vise" };

        GetActivityLogsQueryHandler.ResolveUserName(log).Should().Be(GetActivityLogsQueryHandler.DeletedUserLabel);
    }

    [Fact]
    public void Un_compte_reference_mais_non_charge_et_un_refus_sans_adresse_ont_un_libelle_explicite()
    {
        GetActivityLogsQueryHandler.ResolveUserName(new AuditLog { UserId = 5, User = null, Action = "login" })
            .Should().Be("Utilisateur n° 5");

        foreach (var vide in new[] { null, "", "   " })
            GetActivityLogsQueryHandler.ResolveUserName(new AuditLog { UserId = null, Action = LoginCommandHandler.FailedLoginAction, EntityName = vide })
                .Should().Be("Adresse inconnue");
    }

    /// <summary>
    /// Les noms d'actions reconnus par ResolveUserName sont des littéraux, comme chez leurs
    /// écrivains : un renommage ou un nouvel écrivain ferait repasser en « Système » les
    /// actions d'un compte supprimé sans qu'aucun autre test ne le voie. Relève donc dans le
    /// code source chaque écriture d'AuditLog (et toute insertion SQL brute) et exige qu'elle
    /// soit classée : action écrite avec son auteur, ou refus de connexion.
    /// </summary>
    [Fact]
    public void Chaque_ecrivain_d_audit_logs_ecrit_une_action_classee()
    {
        var services = FindServicesDirectory();
        var writer = new Regex(@"\bnew\s+(?:GisAPI\.Domain\.Entities\.)?AuditLog\s*(?:\(\s*\))?\s*\{");
        var actionAssignment = new Regex(@"\bAction\s*=\s*(?<expr>[^,\r\n}]+)");
        var middlewareVerb = new Regex(@"(?:""[A-Z]+""|_)\s*=>\s*""(?<verb>[a-z]+)""");
        var rawInsert = new Regex(@"INSERT\s+INTO\s+""?audit_logs", RegexOptions.IgnoreCase);

        var seen = new List<string>();
        var unclassified = new List<string>();

        foreach (var file in SourceFiles(services, "*.cs", "src", "GisAPI")
                     .Concat(SourceFiles(services, "*.rs", Path.Combine("gps-ingest-rust", "src"))))
        {
            var code = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            if (rawInsert.IsMatch(code))
                unclassified.Add($"{name} : insertion SQL brute dans audit_logs");

            foreach (Match m in writer.Matches(code))
            {
                var start = m.Index + m.Length;
                var assignment = actionAssignment.Match(code, start);
                if (!assignment.Success || assignment.Index - start > 1500)
                {
                    unclassified.Add($"{name} : AuditLog sans Action lisible");
                    continue;
                }

                var expr = assignment.Groups["expr"].Value.Trim();
                seen.Add($"{name} : {expr}");

                if (expr == nameof(LoginCommandHandler.FailedLoginAction) || expr.EndsWith("." + nameof(LoginCommandHandler.FailedLoginAction)))
                    continue;

                IEnumerable<string> actions;
                if (expr.Length >= 2 && expr[0] == '"' && expr[^1] == '"')
                    actions = new[] { expr[1..^1] };
                else if (name == "AuditTrailMiddleware.cs" && expr == "action")
                    actions = middlewareVerb.Matches(code).Select(v => v.Groups["verb"].Value + "_vehicles").ToList();
                else
                {
                    unclassified.Add($"{name} : Action = {expr} (expression non reconnue)");
                    continue;
                }

                if (!actions.Any())
                    unclassified.Add($"{name} : aucun verbe relevé dans DescribeRequest");

                foreach (var action in actions)
                    if (GetActivityLogsQueryHandler.ResolveUserName(new AuditLog { UserId = null, Action = action }) != GetActivityLogsQueryHandler.DeletedUserLabel)
                        unclassified.Add($"{name} : « {action} » passerait pour une action du système une fois le compte supprimé");
            }
        }

        // Garde-fou du relevé lui-même : les deux formes d'écriture existantes sont bien vues.
        seen.Should().Contain("AuditTrailMiddleware.cs : action");
        seen.Should().Contain(s => s.StartsWith("LoginCommandHandler.cs : ") && s.EndsWith(nameof(LoginCommandHandler.FailedLoginAction)));

        unclassified.Should().BeEmpty(
            "une action écrite avec l'utilisateur qui agit doit figurer dans UserActions de GetActivityLogsQueryHandler, "
            + "et une écriture sans utilisateur doit être traitée à part dans ResolveUserName");
    }

    private static IEnumerable<string> SourceFiles(string services, string pattern, params string[] roots) =>
        roots.Select(r => Path.Combine(services, r))
            .Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, pattern, SearchOption.AllDirectories))
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(s => s is "bin" or "obj" or "target" or "node_modules"));

    // Chemin du source à la compilation : les tests tournent aussi depuis un OutDir hors du dépôt.
    private static string FindServicesDirectory([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile) ?? ".");
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "src", "GisAPI.Application"))
                                && Directory.Exists(Path.Combine(dir.FullName, "GisAPI"))))
            dir = dir.Parent;

        dir.Should().NotBeNull($"le dossier services/ doit être retrouvé à partir de {thisFile}");
        return dir!.FullName;
    }
}
