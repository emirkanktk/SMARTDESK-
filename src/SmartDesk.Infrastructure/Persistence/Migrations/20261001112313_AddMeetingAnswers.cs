using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeclineReason",
                table: "ReservationAttendees",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedAt",
                table: "ReservationAttendees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Response",
                table: "ReservationAttendees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "InboxMessages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeclineReason",
                table: "ReservationAttendees");

            migrationBuilder.DropColumn(
                name: "RespondedAt",
                table: "ReservationAttendees");

            migrationBuilder.DropColumn(
                name: "Response",
                table: "ReservationAttendees");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "InboxMessages");
        }
    }
}
