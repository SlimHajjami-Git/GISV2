using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace GisAPI.Infrastructure.Persistence.Migrations;

public partial class AddChatMessages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "chat_messages",
            columns: table => new
            {
                id = table.Column<int>(nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                sender_id = table.Column<int>(nullable: false),
                receiver_id = table.Column<int>(nullable: false),
                content = table.Column<string>(type: "text", nullable: false),
                is_read = table.Column<bool>(nullable: false, defaultValue: false),
                read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                company_id = table.Column<int>(nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_chat_messages", x => x.id);
                table.ForeignKey("FK_chat_messages_users_sender_id", x => x.sender_id, "users", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_chat_messages_users_receiver_id", x => x.receiver_id, "users", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_chat_messages_societes_company_id", x => x.company_id, "societes", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("ix_chat_messages_sender_receiver_time", "chat_messages", new[] { "sender_id", "receiver_id", "created_at" });
        migrationBuilder.CreateIndex("ix_chat_messages_receiver_unread", "chat_messages", new[] { "receiver_id", "is_read" });
        migrationBuilder.CreateIndex("ix_chat_messages_company_id", "chat_messages", "company_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("chat_messages");
    }
}
