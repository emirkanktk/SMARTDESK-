using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReminderMinutes",
                table: "Reservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PendingReminders",
                table: "Reservations",
                column: "StartDateTime",
                filter: "\"ReminderMinutes\" IS NOT NULL AND \"ReminderSentAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_PendingReminders",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ReminderMinutes",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "Reservations");
        }
    }
}
