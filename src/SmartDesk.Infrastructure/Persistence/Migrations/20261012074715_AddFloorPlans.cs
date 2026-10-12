using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFloorPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MapX",
                table: "Resources",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MapY",
                table: "Resources",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FloorPlans",
                columns: table => new
                {
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FloorPlans", x => x.Location);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FloorPlans");

            migrationBuilder.DropColumn(
                name: "MapX",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "MapY",
                table: "Resources");
        }
    }
}
