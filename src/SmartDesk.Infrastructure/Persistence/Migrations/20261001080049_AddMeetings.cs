using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxMessages_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ReservationAttendees",
                columns: table => new
                {
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationAttendees", x => new { x.ReservationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ReservationAttendees_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_RecipientId_CreatedAt",
                table: "InboxMessages",
                columns: new[] { "RecipientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ReservationId",
                table: "InboxMessages",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_Unread",
                table: "InboxMessages",
                column: "RecipientId",
                filter: "\"ReadAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationAttendees_UserId",
                table: "ReservationAttendees",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages");

            migrationBuilder.DropTable(
                name: "ReservationAttendees");
        }
    }
}
