using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresApproval",
                table: "ResourceTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAt",
                table: "Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DecidedByUserId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeclineReason",
                table: "Reservations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedAt",
                table: "Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a01"),
                column: "RequiresApproval",
                value: false);

            migrationBuilder.UpdateData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a02"),
                column: "RequiresApproval",
                value: false);

            migrationBuilder.UpdateData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a03"),
                column: "RequiresApproval",
                value: false);

            migrationBuilder.UpdateData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a04"),
                column: "RequiresApproval",
                value: false);

            migrationBuilder.UpdateData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a05"),
                column: "RequiresApproval",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_AwaitingApproval",
                table: "Reservations",
                column: "StartDateTime",
                filter: "\"Status\" = 2");

            // A request waiting for approval (2) holds its time as a booking (0) does: the overlap
            // constraint (ADR-0011) covers both, so two of them can't hold the same time either.
            migrationBuilder.Sql("""ALTER TABLE "Reservations" DROP CONSTRAINT IF EXISTS "EX_Reservations_NoOverlap";""");
            migrationBuilder.Sql(
                """
                ALTER TABLE "Reservations"
                    ADD CONSTRAINT "EX_Reservations_NoOverlap"
                    EXCLUDE USING gist (
                        "ResourceId" WITH =,
                        tstzrange("StartDateTime", "EndDateTime", '[)') WITH &&
                    )
                    WHERE ("Status" IN (0, 2));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to Confirmed rows only, as AddReservationOverlapConstraint made it.
            migrationBuilder.Sql("""ALTER TABLE "Reservations" DROP CONSTRAINT IF EXISTS "EX_Reservations_NoOverlap";""");
            migrationBuilder.Sql(
                """
                ALTER TABLE "Reservations"
                    ADD CONSTRAINT "EX_Reservations_NoOverlap"
                    EXCLUDE USING gist (
                        "ResourceId" WITH =,
                        tstzrange("StartDateTime", "EndDateTime", '[)') WITH &&
                    )
                    WHERE ("Status" = 0);
                """);

            migrationBuilder.DropIndex(
                name: "IX_Reservations_AwaitingApproval",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RequiresApproval",
                table: "ResourceTypes");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DecidedByUserId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DeclineReason",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RequestedAt",
                table: "Reservations");
        }
    }
}
