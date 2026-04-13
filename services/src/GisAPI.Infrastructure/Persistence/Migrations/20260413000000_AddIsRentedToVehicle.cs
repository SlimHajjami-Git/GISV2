using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIsRentedToVehicle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_rented",
                table: "vehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "is_rented", table: "vehicles");
        }
    }
}
