using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Creates the user_device_tokens table used to store FCM device tokens
/// for mobile push notifications (remote stop approval flow, etc.).
///
/// A token is owned by one user; CompanyId is derived via the user row,
/// so this table is intentionally not multi-tenant scoped at the column
/// level. The unique index on (token) enforces that a physical device
/// is registered to at most one user at a time — re-installing the app
/// after switching user must replace the previous row (upsert on token).
///
/// The [DbContext] + [Migration] attributes are required for EF Core's
/// runtime IMigrationsAssembly to discover this class — without them,
/// context.Database.MigrateAsync() on startup would silently skip the
/// migration (ask any previous handwritten migration in this folder).
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260420143133_AddUserDeviceTokens")]
public partial class AddUserDeviceTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_device_tokens",
            columns: table => new
            {
                id = table.Column<int>(nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<int>(nullable: false),
                token = table.Column<string>(type: "text", nullable: false),
                platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "android"),
                is_active = table.Column<bool>(nullable: false, defaultValue: true),
                registered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_device_tokens", x => x.id);
                table.ForeignKey(
                    name: "FK_user_device_tokens_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        // One physical device token is unique across the whole fleet:
        // if the same token ever comes back for another user we must
        // replace (upsert) rather than end up with two active rows.
        migrationBuilder.CreateIndex(
            name: "IX_user_device_tokens_token",
            table: "user_device_tokens",
            column: "token",
            unique: true);

        // Hot path: "find all active tokens for user X so I can push to
        // every device they're logged into."
        migrationBuilder.CreateIndex(
            name: "IX_user_device_tokens_user_id_is_active",
            table: "user_device_tokens",
            columns: new[] { "user_id", "is_active" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_device_tokens");
    }
}
