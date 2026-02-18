using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace GisAPI.Infrastructure.Persistence.Migrations;

public partial class AddAiChatMessages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ai_chat_messages",
            columns: table => new
            {
                id = table.Column<int>(nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<int>(nullable: false),
                vehicle_id = table.Column<int>(nullable: false),
                role = table.Column<string>(maxLength: 20, nullable: false),
                content = table.Column<string>(type: "text", nullable: false),
                session_id = table.Column<string>(maxLength: 100, nullable: true),
                tokens_used = table.Column<int>(nullable: true),
                company_id = table.Column<int>(nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ai_chat_messages", x => x.id);
                table.ForeignKey("FK_ai_chat_messages_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ai_chat_messages_vehicles_vehicle_id", x => x.vehicle_id, "vehicles", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ai_chat_messages_societes_company_id", x => x.company_id, "societes", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("ix_ai_chat_messages_user_vehicle_time", "ai_chat_messages", new[] { "user_id", "vehicle_id", "created_at" });
        migrationBuilder.CreateIndex("ix_ai_chat_messages_company_id", "ai_chat_messages", "company_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ai_chat_messages");
    }
}
