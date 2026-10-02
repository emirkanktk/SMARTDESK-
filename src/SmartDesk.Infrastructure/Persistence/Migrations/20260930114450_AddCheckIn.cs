using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing resource gets its own random code (ADR-0030): a volatile default is
            // worked out row by row, and a random UUID without its dashes is 32 hex digits, the
            // shape CheckInRules.NewCode makes. The default is dropped again right after; new
            // resources get their code from the app.
            migrationBuilder.AddColumn<string>(
                name: "CheckInCode",
                table: "Resources",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValueSql: "replace(gen_random_uuid()::text, '-', '')");
            migrationBuilder.Sql("ALTER TABLE \"Resources\" ALTER COLUMN \"CheckInCode\" DROP DEFAULT;");

            migrationBuilder.AddColumn<bool>(
                name: "CheckInRequired",
                table: "Resources",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CheckInRequired",
                table: "Reservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAt",
                table: "Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleasedAt",
                table: "Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_AwaitingCheckIn",
                table: "Reservations",
                column: "StartDateTime",
                filter: "\"CheckInRequired\" AND \"CheckedInAt\" IS NULL AND \"Status\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_AwaitingCheckIn",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CheckInCode",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "CheckInRequired",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "CheckInRequired",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CheckedInAt",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ReleasedAt",
                table: "Reservations");
        }
    }
}
