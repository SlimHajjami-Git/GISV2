using FluentAssertions;
using GisAPI.Middleware;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// Filet de sécurité du 03/10/2026 : une violation d'unicité de la base (23505) ne doit
/// plus JAMAIS ressortir en « An unexpected error occurred ». Les handlers contrôlent en
/// amont ; ceci rattrape la course entre deux enregistrements simultanés et tout chemin
/// qui aurait oublié le contrôle — c'est ce qui a coûté une recette à HERTZ.
/// </summary>
public class DoublonBaseEnConflitTests
{
    private static PostgresException Postgres(string sqlState, string? contrainte) =>
        new("duplicate key value violates unique constraint", "ERROR", "ERROR", sqlState,
            constraintName: contrainte);

    private static DbUpdateException Enregistrement(PostgresException cause) =>
        new("An error occurred while saving the entity changes.", cause);

    [Fact]
    public void Le_doublon_d_email_rend_la_phrase_que_l_utilisateur_peut_corriger()
    {
        var message = ExceptionHandlingMiddleware.MessageDeDoublon(
            Enregistrement(Postgres(PostgresErrorCodes.UniqueViolation, "idx_users_email")));

        message.Should().Be("Cet email est déjà utilisé");
    }

    [Fact]
    public void Deux_creations_simultanees_de_la_meme_plaque_rendent_la_phrase_du_matricule()
    {
        // Migration 062 : le double clic sur « Ajouter » (campagne GPA, n°16) — la seconde
        // création heurte l'index unique et doit le dire comme le contrôle de l'API.
        var message = ExceptionHandlingMiddleware.MessageDeDoublon(
            Enregistrement(Postgres(PostgresErrorCodes.UniqueViolation, "ux_vehicles_societe_plaque")));

        message.Should().Be("Ce matricule est déjà utilisé par un autre véhicule de la société.");
    }

    [Fact]
    public void Une_contrainte_inconnue_reste_un_doublon_annonce_comme_tel()
    {
        var message = ExceptionHandlingMiddleware.MessageDeDoublon(
            Enregistrement(Postgres(PostgresErrorCodes.UniqueViolation, "idx_quelque_chose")));

        message.Should().Be("Cette valeur est déjà utilisée",
            "mieux vaut une phrase vague qu'un 500 : l'utilisateur sait au moins quoi changer");
    }

    [Fact]
    public void La_cause_est_cherchee_en_profondeur_dans_les_exceptions_imbriquees()
    {
        var enfoui = new InvalidOperationException("couche intermédiaire",
            Enregistrement(Postgres(PostgresErrorCodes.UniqueViolation, "idx_users_email")));

        ExceptionHandlingMiddleware.MessageDeDoublon(enfoui).Should().Be("Cet email est déjà utilisé");
    }

    [Fact]
    public void Une_autre_erreur_Postgres_n_est_pas_un_doublon_et_garde_son_traitement()
    {
        // 23503 = clé étrangère. Ce n'est pas une valeur déjà prise : ne pas la déguiser.
        ExceptionHandlingMiddleware.MessageDeDoublon(
            Enregistrement(Postgres(PostgresErrorCodes.ForeignKeyViolation, "FK_users_roles_role_id")))
            .Should().BeNull();
    }

    [Fact]
    public void Une_exception_ordinaire_n_est_pas_un_doublon()
    {
        ExceptionHandlingMiddleware.MessageDeDoublon(new InvalidOperationException("rien à voir"))
            .Should().BeNull();
    }
}
