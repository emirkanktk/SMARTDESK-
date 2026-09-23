using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDesk.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Makes "no overlapping confirmed reservations for the same resource" a database
    /// guarantee rather than only an application check (ADR-0011).
    /// </summary>
    public partial class AddReservationOverlapConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_EndAfterStart",
                table: "Reservations",
                sql: "\"EndDateTime\" > \"StartDateTime\"");

            // btree_gist lets a GiST index combine plain equality ("ResourceId" =) with range
            // overlap (&&). It ships with PostgreSQL and is a trusted extension, so the
            // database owner can create it without superuser rights.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // '[)' = half-open range, matching SmartDesk.Domain.Rules.ReservationOverlap: a
            // booking ending at 11:00 doesn't collide with one starting at 11:00. Only
            // Confirmed (0) rows take part, so cancelled reservations never block anything.
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "Reservations" DROP CONSTRAINT IF EXISTS "EX_Reservations_NoOverlap";""");

            // The btree_gist extension is left installed: other objects may depend on it,
            // and it's harmless on its own.

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_EndAfterStart",
                table: "Reservations");
        }
    }
}
