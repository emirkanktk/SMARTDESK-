using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDoorScreen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DoorScreenSetUpAt",
                table: "Resources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoorScreenTokenHash",
                table: "Resources",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CheckedInByUserId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseWarningSentAt",
                table: "Reservations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DoorScreenSetUpAt",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "DoorScreenTokenHash",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "CheckedInByUserId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ReleaseWarningSentAt",
                table: "Reservations");
        }
    }
}
