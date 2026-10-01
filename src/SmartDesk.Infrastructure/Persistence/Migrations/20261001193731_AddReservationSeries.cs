using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                table: "InboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReservationSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    DaysOfWeek = table.Column<int>(type: "integer", nullable: false),
                    Until = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationSeries", x => x.Id);
                    table.CheckConstraint("CK_ReservationSeries_Interval", "\"Interval\" BETWEEN 1 AND 4");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_SeriesId",
                table: "Reservations",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_SeriesId",
                table: "InboxMessages",
                column: "SeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_InboxMessages_ReservationSeries_SeriesId",
                table: "InboxMessages",
                column: "SeriesId",
                principalTable: "ReservationSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_ReservationSeries_SeriesId",
                table: "Reservations",
                column: "SeriesId",
                principalTable: "ReservationSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InboxMessages_ReservationSeries_SeriesId",
                table: "InboxMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_ReservationSeries_SeriesId",
                table: "Reservations");

            migrationBuilder.DropTable(
                name: "ReservationSeries");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_SeriesId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_InboxMessages_SeriesId",
                table: "InboxMessages");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "InboxMessages");
        }
    }
}
