using FluentAssertions;
using GisAPI.Application.Features.Admin.Companies.Commands.ResetCompanyData;
using Xunit;

namespace GisAPI.Tests.Application.Admin;

/// <summary>
/// Le plan de remise à zéro est calculé sur le VRAI catalogue de TN (fixture du 10/09/2026),
/// pour que chaque garantie soit prouvée sur le schéma de production, pas sur un schéma jouet.
/// </summary>
public class CompanyDataResetPlannerTests
{
    private static readonly ResetPlan Plan = CompanyDataResetPlanner.Plan(TnCatalogFixture.Load());

    private static int IndexOf(string table) => Plan.Steps.ToList().FindIndex(s => s.Table == table);
    private static ResetStep Step(string table) => Plan.Steps.Single(s => s.Table == table);

    [Fact]
    public void Le_noyau_de_la_societe_n_est_jamais_dans_le_plan()
    {
        var tables = Plan.Steps.Select(s => s.Table).ToList();
        tables.Should().NotContain(new[] { "societes", "users", "roles", "refresh_tokens", "user_device_tokens",
            "gps_devices", "gps_positions", "subscription_orders", "subscription_types" });
        Plan.Protected.Should().Contain(new[] { "societes", "users", "roles", "gps_devices", "gps_positions" });
    }

    [Fact]
    public void Les_tables_de_sauvegarde_copiees_a_la_main_sont_ignorees()
    {
        Plan.Skipped.Should().Contain("gps_devices_orphans_bak_20260715");
        Plan.Steps.Select(s => s.Table).Should().NotContain("gps_devices_orphans_bak_20260715");
    }

    [Fact]
    public void Tout_ce_qui_a_ete_supprime_a_la_main_le_10_09_est_couvert()
    {
        // Les 25 tables non vides de la société 14 ce jour-là (subscription_orders exclu : historique d'abonnement).
        var expected = new[]
        {
            "maintenance_logs", "vehicle_maintenance_schedules", "maintenance_templates", "repair_parts", "repairs",
            "acquisition_payments", "vehicle_costs", "fuel_entries", "accident_events", "ai_chat_messages", "chat_messages",
            "user_vehicles", "drivers", "vehicles", "departments", "suppliers", "fuel_pricing", "alert_emails",
            "notifications", "audit_logs",
        };
        Plan.Steps.Select(s => s.Table).Should().Contain(expected);
    }

    [Fact]
    public void Les_tables_sans_colonne_societe_sont_atteintes_par_leurs_cles_etrangeres()
    {
        Step("vehicle_documents").Where.Should().Contain("\"vehicle_id\" IN (SELECT \"id\" FROM \"vehicles\" WHERE");
        Step("repair_parts").Where.Should().Contain("\"repair_id\" IN (SELECT \"id\" FROM \"repairs\" WHERE");
        Step("accident_event_documents").Where.Should().Contain("\"accident_event_id\" IN (SELECT \"id\" FROM \"accident_events\"");
        Step("tour_waypoints").Where.Should().Contain("\"TourId\" IN (SELECT \"Id\" FROM \"tours\"");
    }

    [Fact]
    public void Une_colonne_societe_vide_est_rattrapee_par_le_vehicule_sans_toucher_une_autre_societe()
    {
        // maintenance_logs.company_id vaut 0 partout en production : le chemin par le véhicule est
        // indispensable, mais seulement quand la colonne société est vide (0 ou NULL).
        var where = Step("maintenance_logs").Where;
        where.Should().StartWith("\"company_id\" = {0} OR ((");
        where.Should().Contain("(\"company_id\" = 0 OR \"company_id\" IS NULL)");
        where.Should().Contain("\"vehicle_id\" IN (SELECT \"id\" FROM \"vehicles\" WHERE");
    }

    [Fact]
    public void Deux_colonnes_societe_sont_combinees_par_OU()
    {
        Step("audit_logs").Where.Should().Contain("\"CompanyId\" = {0}").And.Contain("\"SocieteId\" = {0}");
        Step("suppliers").Where.Should().Contain("\"CompanyId\" = {0}").And.Contain("\"SocieteId\" = {0}");
    }

    [Fact]
    public void Les_enfants_passent_avant_les_parents()
    {
        IndexOf("vehicle_costs").Should().BeLessThan(IndexOf("vehicles"));
        IndexOf("acquisition_payments").Should().BeLessThan(IndexOf("vehicles"));
        IndexOf("ai_chat_messages").Should().BeLessThan(IndexOf("vehicles"), "ai_chat_messages.vehicle_id est RESTRICT");
        IndexOf("gps_alerts").Should().BeLessThan(IndexOf("vehicles"), "gps_alerts.vehicle_id est NO ACTION");
        IndexOf("vehicles").Should().BeLessThan(IndexOf("departments"), "vehicles.DepartmentId est NO ACTION");
        IndexOf("repair_parts").Should().BeLessThan(IndexOf("repairs"));
        IndexOf("estimate_items").Should().BeLessThan(IndexOf("estimates"));
        IndexOf("AccidentClaimDocuments").Should().BeLessThan(IndexOf("AccidentClaims"));
        IndexOf("trip_waypoints").Should().BeLessThan(IndexOf("trips"), "CASCADE aussi : les comptes de l'aperçu sont ceux de nos ordres");
        IndexOf("trips").Should().BeLessThan(IndexOf("vehicles"));
        IndexOf("vehicle_documents").Should().BeLessThan(IndexOf("vehicles"));
        IndexOf("maintenance_logs").Should().BeLessThan(IndexOf("suppliers"), "maintenance_logs.supplier_id est SET NULL, on supprime quand même l'enfant d'abord");
    }

    [Fact]
    public void Chaque_table_est_supprimee_avant_tous_ses_parents_du_plan()
    {
        var catalog = TnCatalogFixture.Load();
        var order = Plan.Steps.Select(s => s.Table).ToList();
        foreach (var fk in catalog.ForeignKeys)
        {
            if (fk.Child == fk.Parent) continue;
            var c = order.IndexOf(fk.Child); var p = order.IndexOf(fk.Parent);
            if (c < 0 || p < 0) continue;
            // Seule exception tolérée : la coupure d'un cycle SET NULL (drivers ⇄ vehicles).
            if (fk.DeleteRule is 'n' or 'd' && catalog.ForeignKeys.Any(o => o.Child == fk.Parent && o.Parent == fk.Child)) continue;
            c.Should().BeLessThan(p, fk.Child + "." + fk.ChildColumn + " -> " + fk.Parent);
        }
    }

    [Fact]
    public void Un_garde_fou_protege_l_historique_d_une_autre_societe_sous_chaque_cascade()
    {
        // trips.VehicleId → vehicles (CASCADE) et trips porte CompanyId : un véhicule transféré
        // emporterait les trajets de son ancienne société. Le garde-fou compte ces lignes-là.
        var g = Plan.Guards.Single(g => g.Table == "trips");
        g.Where.Should().Contain("\"VehicleId\" IN (SELECT \"id\" FROM \"vehicles\" WHERE");
        g.Where.Should().Contain("AND NOT (\"CompanyId\" = {0} OR \"CompanyId\" = 0 OR \"CompanyId\" IS NULL)");
        Plan.Guards.Select(x => x.Table).Should().Contain(new[] { "vehicle_costs", "acquisition_payments", "repairs", "fuel_records", "maintenance_logs" });
        Plan.Guards.Should().NotContain(x => x.Table == "vehicle_documents", "sans colonne société, rien à protéger");
        Plan.Guards.Should().NotContain(x => x.Table == "drivers", "drivers.assigned_vehicle_id est SET NULL, pas CASCADE");
    }

    [Fact]
    public void Le_cycle_conducteur_vehicule_ne_bloque_pas()
    {
        // drivers.assigned_vehicle_id → vehicles et vehicles.assigned_driver_id → drivers, tous deux SET NULL.
        Plan.Steps.Select(s => s.Table).Should().Contain(new[] { "drivers", "vehicles" });
        Step("drivers").Where.Should().NotContain("FROM \"drivers\"", "on ne repasse pas par soi-même");
    }

    [Fact]
    public void Le_predicat_ne_suit_jamais_une_table_protegee()
    {
        // notifications.UserId → users, vehicle_costs.created_by_user_id → users : les utilisateurs
        // sont conservés, donc on ne cible jamais « les lignes des utilisateurs de la société ».
        foreach (var step in Plan.Steps)
            step.Where.Should().NotContain("FROM \"users\"", step.Table);
    }

    [Fact]
    public void Les_colonnes_de_fichiers_sont_reperees_pour_le_nettoyage_du_disque()
    {
        Step("vehicle_costs").FileColumns.Should().Contain("receipt_url");
        Step("accident_events").FileColumns.Should().Contain("pdf_report_url");
        Step("maintenance_logs").FileColumns.Should().Contain("photos");
        Step("vehicle_documents").FileColumns.Should().Contain("file_url");
        Step("notifications").FileColumns.Should().Contain("ActionUrl", "large exprès : la valeur est filtrée à la lecture (/uploads/ seulement)");
    }

    [Fact]
    public void Chaque_ordre_est_parametre_et_ne_contient_aucun_identifiant_en_dur()
    {
        foreach (var step in Plan.Steps)
        {
            step.Where.Should().Contain("{0}", step.Table);
            step.Where.Should().NotMatchRegex(@"=\s*\d{2,}", step.Table);
        }
    }

    [Fact]
    public void Une_table_protegee_qui_bloquerait_la_suppression_fait_echouer_le_plan()
    {
        var tables = new List<CatalogTable>
        {
            new("vehicles", new[] { "company_id" }, new[] { "id", "company_id" }),
            new("users", new[] { "company_id" }, new[] { "id", "company_id", "favorite_vehicle_id" }),
        };
        var fks = new List<CatalogForeignKey> { new("users", "favorite_vehicle_id", "vehicles", "id", 'a') };

        var act = () => CompanyDataResetPlanner.Plan(new CompanyDataCatalog(tables, fks));
        act.Should().Throw<InvalidOperationException>().WithMessage("*users*vehicles*");
    }

    [Fact]
    public void Les_colonnes_de_fichiers_en_PascalCase_sont_reperees_aussi()
    {
        var tables = new List<CatalogTable>
        {
            new("Claims", new[] { "CompanyId" }, new[] { "Id", "CompanyId", "FileUrl", "PhotoPath", "Title" }),
        };
        var plan = CompanyDataResetPlanner.Plan(new CompanyDataCatalog(tables, new List<CatalogForeignKey>()));
        plan.Steps.Single().FileColumns.Should().BeEquivalentTo(new[] { "FileUrl", "PhotoPath" });
    }

    [Fact]
    public void Une_table_protegee_en_cascade_sur_une_table_supprimee_fait_aussi_echouer_le_plan()
    {
        var tables = new List<CatalogTable>
        {
            new("vehicles", new[] { "company_id" }, new[] { "id", "company_id" }),
            new("users", new[] { "company_id" }, new[] { "id", "company_id", "vehicle_id" }),
        };
        var fks = new List<CatalogForeignKey> { new("users", "vehicle_id", "vehicles", "id", 'c') };

        var act = () => CompanyDataResetPlanner.Plan(new CompanyDataCatalog(tables, fks));
        act.Should().Throw<InvalidOperationException>().WithMessage("*users*vehicles*");
    }

    [Fact]
    public void Un_cycle_bloquant_entre_deux_tables_supprimees_est_refuse()
    {
        var tables = new List<CatalogTable>
        {
            new("a", new[] { "company_id" }, new[] { "id", "company_id", "b_id" }),
            new("b", new[] { "company_id" }, new[] { "id", "company_id", "a_id" }),
        };
        var fks = new List<CatalogForeignKey> { new("a", "b_id", "b", "id", 'a'), new("b", "a_id", "a", "id", 'r') };

        var act = () => CompanyDataResetPlanner.Plan(new CompanyDataCatalog(tables, fks));
        act.Should().Throw<InvalidOperationException>().WithMessage("*Cycle*");
    }
}
