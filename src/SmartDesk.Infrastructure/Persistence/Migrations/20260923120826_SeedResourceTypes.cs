using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SmartDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedResourceTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ResourceTypes",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a01"), "Meeting Room" },
                    { new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a02"), "Desk" },
                    { new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a03"), "Company Car" },
                    { new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a04"), "Projector" },
                    { new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a05"), "Camera" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTypes_Name",
                table: "ResourceTypes",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResourceTypes_Name",
                table: "ResourceTypes");

            migrationBuilder.DeleteData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a01"));

            migrationBuilder.DeleteData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a02"));

            migrationBuilder.DeleteData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a03"));

            migrationBuilder.DeleteData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a04"));

            migrationBuilder.DeleteData(
                table: "ResourceTypes",
                keyColumn: "Id",
                keyValue: new Guid("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a05"));
        }
    }
}
