using FluentAssertions;
using GisAPI.Application.Common.Helpers;
using GisAPI.Tests.Application.Admin;
using Xunit;

namespace GisAPI.Tests.Application.Users;

/// <summary>
/// USR-DEL (préexistant, corrigé le 16/09/2026) : un compte qui s'était connecté une fois
/// ne pouvait plus être supprimé. La connexion écrit audit_logs.UserId, clé étrangère en
/// NO ACTION, comme une douzaine d'autres colonnes qui référencent users(id) sans cascade : 23503, donc 500.
///
/// Ici, la construction des instructions à partir des catalogues réels : TN (TnCatalogFixture,
/// relevé du 10/09/2026) et la base locale (copie du schéma de DZ, relevé du 16/09/2026).
/// </summary>
public class UserDeletionHelperTests
{
    private const string DeleteUser = "DELETE FROM \"users\" WHERE id = {0}";

    /// <summary>
    /// TnCatalogFixture ne porte pas la nullabilité : ces colonnes sont NOT NULL dans la base
    /// locale, avec la même règle ON DELETE que sur TN ; toutes les autres sont nullables.
    /// </summary>
    private static readonly HashSet<string> NotNullColumns = new()
    {
        "ai_chat_messages.user_id", "chat_messages.receiver_id", "chat_messages.sender_id",
        "driver_assignments.DriverId", "driver_scores.DriverId", "notifications.UserId",
        "refresh_tokens.UserId", "report_schedules.CreatedByUserId", "user_device_tokens.user_id",
        "user_vehicles.user_id", "vehicle_user_assignments.user_id",
    };

    /// <summary>Clés étrangères vers users(id) de la base locale (pg_constraint, 16/09/2026).</summary>
    private static readonly string[] LocalCatalog =
    {
        "YES|a|AccidentClaims.CreatedByUserId", "YES|a|AccidentClaims.DriverId",
        "NO|r|ai_chat_messages.user_id", "YES|a|audit_logs.UserId",
        "NO|r|chat_messages.receiver_id", "NO|r|chat_messages.sender_id",
        "YES|a|driver_assignments.AssignedByUserId", "NO|c|driver_assignments.DriverId",
        "NO|c|driver_scores.DriverId", "YES|c|drivers.user_id",
        "YES|a|driving_events.DriverId", "YES|n|fuel_entries.driver_id",
        "YES|a|fuel_records.driver_id", "YES|a|gps_alerts.resolved_by_user_id",
        "NO|c|notifications.UserId", "YES|n|part_transactions.created_by_user_id",
        "NO|c|refresh_tokens.UserId", "NO|c|report_schedules.CreatedByUserId",
        "YES|a|reports.CreatedByUserId", "YES|a|reservations.ApprovedByUserId",
        "YES|a|reservations.AssignedDriverId", "YES|a|reservations.RequestedByUserId",
        "YES|a|speed_limit_alerts.AcknowledgedById", "YES|a|trips.DriverId",
        "NO|c|user_device_tokens.user_id", "YES|n|user_vehicles.assigned_by",
        "NO|c|user_vehicles.user_id", "YES|a|vehicle_costs.created_by_user_id",
        "YES|a|vehicle_stops.driver_id", "NO|c|vehicle_user_assignments.user_id",
        "YES|n|vehicles.assigned_supervisor_id",
    };

    private static string[] TnCatalog() => TnCatalogFixture.Load().ForeignKeys
        .Where(fk => fk.Parent == "users")
        .Select(fk => $"{(NotNullColumns.Contains($"{fk.Child}.{fk.ChildColumn}") ? "NO" : "YES")}|{fk.DeleteRule}|{fk.Child}.{fk.ChildColumn}")
        .ToArray();

    private static string SetNull(string table, string column) =>
        $"UPDATE \"{table}\" SET \"{column}\" = NULL WHERE \"{column}\" = {{0}}";

    private static string Delete(string table, string column) =>
        $"DELETE FROM \"{table}\" WHERE \"{column}\" = {{0}}";

    public static IEnumerable<object[]> Catalogs() => new[]
    {
        new object[] { "TN", TnCatalog() },
        new object[] { "local", LocalCatalog },
    };

    [Fact]
    public void Le_catalogue_TN_porte_les_vingt_et_une_cles_etrangeres_vers_users()
    {
        TnCatalog().Should().BeEquivalentTo(
            "YES|a|AccidentClaims.CreatedByUserId", "NO|r|ai_chat_messages.user_id", "YES|a|audit_logs.UserId",
            "NO|r|chat_messages.receiver_id", "NO|r|chat_messages.sender_id", "YES|a|driver_assignments.AssignedByUserId",
            "YES|a|gps_alerts.resolved_by_user_id", "NO|c|notifications.UserId", "YES|n|part_transactions.created_by_user_id",
            "NO|c|refresh_tokens.UserId", "NO|c|report_schedules.CreatedByUserId", "YES|a|reports.CreatedByUserId",
            "YES|a|reservations.ApprovedByUserId", "YES|a|reservations.RequestedByUserId",
            "YES|a|speed_limit_alerts.AcknowledgedById", "NO|c|user_device_tokens.user_id", "YES|n|user_vehicles.assigned_by",
            "NO|c|user_vehicles.user_id", "YES|a|vehicle_costs.created_by_user_id", "NO|c|vehicle_user_assignments.user_id",
            "YES|n|vehicles.assigned_supervisor_id");
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void Chaque_colonne_nullable_est_detachee_quelle_que_soit_sa_regle(string catalogue, string[] rows)
    {
        var statements = UserDeletionHelper.BuildStatements(rows);

        var nullable = rows.Where(r => r.StartsWith("YES|")).Select(r => r.Split('|')[2].Split('.')).ToList();
        nullable.Should().NotBeEmpty(catalogue);
        foreach (var col in nullable)
            statements.Should().Contain(SetNull(col[0], col[1]), $"{catalogue} : {col[0]}.{col[1]} est nullable");

        statements.Should().Contain(SetNull("audit_logs", "UserId"));
        statements.Should().Contain(SetNull("vehicle_costs", "created_by_user_id"));
        // Aucune ligne d'historique n'est supprimée.
        statements.Where(s => s.StartsWith("DELETE")).Should().BeEquivalentTo(
            Delete("ai_chat_messages", "user_id"),
            Delete("chat_messages", "receiver_id"),
            Delete("chat_messages", "sender_id"),
            DeleteUser);
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void Les_colonnes_obligatoires_en_cascade_sont_laissees_a_la_base(string catalogue, string[] rows)
    {
        var statements = UserDeletionHelper.BuildStatements(rows);

        foreach (var table in new[] { "notifications", "refresh_tokens", "report_schedules", "user_device_tokens", "vehicle_user_assignments", "driver_scores" })
            statements.Should().NotContain(s => s.Contains($"\"{table}\""), $"{catalogue} : {table} part en cascade");
        statements.Should().NotContain(s => s.Contains("\"user_vehicles\"") && s.Contains("\"user_id\""));
        statements.Should().NotContain(s => s.Contains("\"driver_assignments\"") && s.Contains("\"DriverId\""));

        // Une instruction par colonne nullable ou obligatoire en NO ACTION / RESTRICT, puis le compte.
        statements.Should().HaveCount(rows.Count(r => r.StartsWith("YES|") || r.StartsWith("NO|a|") || r.StartsWith("NO|r|")) + 1);
        statements.Last().Should().Be(DeleteUser);
        statements.Count(s => s == DeleteUser).Should().Be(1);
    }

    [Fact]
    public void La_fiche_chauffeur_liee_au_compte_est_conservee_malgre_sa_cle_en_cascade()
    {
        var statements = UserDeletionHelper.BuildStatements(LocalCatalog);

        statements.Should().Contain(SetNull("drivers", "user_id"));
        statements.Should().NotContain(s => s.StartsWith("DELETE") && s.Contains("\"drivers\""));
        statements.IndexOf(SetNull("drivers", "user_id")).Should().BeLessThan(statements.IndexOf(DeleteUser));
    }

    [Fact]
    public void Une_reference_obligatoire_vers_un_autre_compte_ne_supprime_jamais_de_compte()
    {
        var statements = UserDeletionHelper.BuildStatements(new[]
        {
            "NO|a|users.created_by_id",
            "YES|a|users.manager_id",
            "ligne illisible",
            "YES|a|sans_colonne",
        });

        statements.Should().Equal(SetNull("users", "manager_id"), DeleteUser);
    }

    /// <summary>
    /// Seules NO ACTION et RESTRICT bloquent la suppression d'une ligne obligatoire. Sur une
    /// colonne NOT NULL en SET NULL ou SET DEFAULT, la base applique sa règle ou refuse (rien
    /// n'est modifié) : supprimer les lignes d'office perdrait ce qu'elle aurait gardé.
    /// Aucune clé de ce type n'existe aujourd'hui, ni en local ni sur TN.
    /// </summary>
    [Fact]
    public void Une_colonne_obligatoire_en_set_null_ou_set_default_est_laissee_a_la_base()
    {
        var statements = UserDeletionHelper.BuildStatements(new[]
        {
            "NO|a|chat_messages.sender_id",
            "NO|r|ai_chat_messages.user_id",
            "NO|n|historique_n.author_id",
            "NO|d|historique_d.author_id",
            "NO|c|jetons.user_id",
        });

        statements.Should().Equal(
            Delete("chat_messages", "sender_id"),
            Delete("ai_chat_messages", "user_id"),
            DeleteUser);
    }

    /// <summary>
    /// La nullabilité réelle de TN n'est pas relevée : si une colonne d'historique y était
    /// NOT NULL sans cascade, la supprimer d'office effacerait des dépenses ou le journal au
    /// lieu de les détacher. Hors messages et conversations, la suppression est refusée.
    /// </summary>
    [Theory]
    [InlineData("NO|a|audit_logs.UserId", "audit_logs")]
    [InlineData("NO|r|vehicle_costs.created_by_user_id", "vehicle_costs")]
    public void Un_historique_obligatoire_hors_messages_refuse_la_suppression_sans_rien_effacer(string row, string table)
    {
        var build = () => UserDeletionHelper.BuildStatements(new[] { "YES|a|reports.CreatedByUserId", row });

        build.Should().Throw<GisAPI.Domain.Exceptions.DomainException>()
            .WithMessage($"Suppression impossible*{table}*Rien n'a été modifié.");
    }
}
