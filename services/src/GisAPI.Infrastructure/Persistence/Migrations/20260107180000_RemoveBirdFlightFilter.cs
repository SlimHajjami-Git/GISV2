using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBirdFlightFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop Bird Flight columns from gps_positions table
            migrationBuilder.DropColumn(
                name: "is_bird_flight",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "bird_flight_reason",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "implicit_speed_kph",
                table: "gps_positions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-add Bird Flight columns if rolling back
            migrationBuilder.AddColumn<bool>(
                name: "is_bird_flight",
                table: "gps_positions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "bird_flight_reason",
                table: "gps_positions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "implicit_speed_kph",
                table: "gps_positions",
                type: "double precision",
                nullable: true);
        }
    }
}


