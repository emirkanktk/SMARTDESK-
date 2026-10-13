using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxActivePerUser",
                table: "ResourceTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxDaysAhead",
                table: "ResourceTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxDurationMinutes",
                table: "ResourceTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OfficeHoursOnly",
                table: "ResourceTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "OfficeHours",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Workdays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeHours", x => x.Id);
                    table.CheckConstraint("CK_OfficeHours_OpensBeforeCloses", "\"OpensAt\" < \"ClosesAt\"");
                    table.CheckConstraint("CK_OfficeHours_Single", "\"Id\" = 1");
                    table.CheckConstraint("CK_OfficeHours_Workdays", "\"Workdays\" BETWEEN 1 AND 127");
                });

            migrationBuilder.InsertData(
                table: "OfficeHours",
                columns: new[] { "Id", "ClosesAt", "OpensAt", "TimeZone", "Workdays" },
                values: new object[] { 1, new TimeOnly(18, 0, 0), new TimeOnly(9, 0, 0), "Europe/Istanbul", 31 });

            migrationBuilder.UpdateData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a01"),
                columns: new[] { "MaxActivePerUser", "MaxDaysAhead", "MaxDurationMinutes", "OfficeHoursOnly" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a02"),
                columns: new[] { "MaxActivePerUser", "MaxDaysAhead", "MaxDurationMinutes", "OfficeHoursOnly" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a03"),
                columns: new[] { "MaxActivePerUser", "MaxDaysAhead", "MaxDurationMinutes", "OfficeHoursOnly" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a04"),
                columns: new[] { "MaxActivePerUser", "MaxDaysAhead", "MaxDurationMinutes", "OfficeHoursOnly" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a05"),
                columns: new[] { "MaxActivePerUser", "MaxDaysAhead", "MaxDurationMinutes", "OfficeHoursOnly" },
                values: new object[] { null, null, null, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfficeHours");

            migrationBuilder.DropColumn(
                name: "MaxActivePerUser",
                table: "ResourceTypes");

            migrationBuilder.DropColumn(
                name: "MaxDaysAhead",
                table: "ResourceTypes");

            migrationBuilder.DropColumn(
                name: "MaxDurationMinutes",
                table: "ResourceTypes");

            migrationBuilder.DropColumn(
                name: "OfficeHoursOnly",
                table: "ResourceTypes");
        }
    }
}
