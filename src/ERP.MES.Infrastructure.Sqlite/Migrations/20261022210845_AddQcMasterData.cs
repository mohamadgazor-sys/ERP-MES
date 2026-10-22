using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddQcMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnitsOfMeasure",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitsOfMeasure", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitsOfMeasure_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuantityTypes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    BaseUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    BaseUnitsPerType = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuantityTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuantityTypes_UnitsOfMeasure_BaseUnitId",
                        column: x => x.BaseUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuantityTypes_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitConversions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    ToUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    Factor = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitConversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitConversions_UnitsOfMeasure_FromUnitId",
                        column: x => x.FromUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitConversions_UnitsOfMeasure_ToUnitId",
                        column: x => x.ToUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitConversions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuantityTypeMembers",
                columns: table => new
                {
                    MemberNo = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityTypeId = table.Column<long>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    BaseUnits = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuantityTypeMembers", x => new { x.QuantityTypeId, x.MemberNo });
                    table.ForeignKey(
                        name: "FK_QuantityTypeMembers_QuantityTypes_QuantityTypeId",
                        column: x => x.QuantityTypeId,
                        principalTable: "QuantityTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "UnitsOfMeasure",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "CreatedByUserId", "IsActive", "Kind", "Name", "UpdatedAtUtc", "Version" },
                values: new object[,]
                {
                    { 1L, "PCS", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, 1, "عدد (قطعه)", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 2L, "PAIR", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, 1, "جفت", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 3L, "KG", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, 2, "کیلوگرم", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 4L, "MM", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, 3, "میلی‌متر", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L }
                });

            migrationBuilder.InsertData(
                table: "QuantityTypes",
                columns: new[] { "Id", "BaseUnitId", "BaseUnitsPerType", "Code", "CreatedAtUtc", "CreatedByUserId", "IsActive", "Name", "UpdatedAtUtc", "Version" },
                values: new object[,]
                {
                    { 1L, 1L, 1, "PIECE", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "قطعه", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 2L, 1L, 2, "PAIR", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "جفت", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L }
                });

            migrationBuilder.InsertData(
                table: "UnitConversions",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Factor", "FromUnitId", "IsActive", "ToUnitId", "UpdatedAtUtc", "Version" },
                values: new object[] { 1L, new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, 2m, 2L, true, 1L, new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L });

            migrationBuilder.CreateIndex(
                name: "IX_QuantityTypes_BaseUnitId",
                table: "QuantityTypes",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_QuantityTypes_Code",
                table: "QuantityTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuantityTypes_CreatedByUserId",
                table: "QuantityTypes",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitConversions_CreatedByUserId",
                table: "UnitConversions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitConversions_FromUnitId_ToUnitId",
                table: "UnitConversions",
                columns: new[] { "FromUnitId", "ToUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitConversions_ToUnitId",
                table: "UnitConversions",
                column: "ToUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitsOfMeasure_Code",
                table: "UnitsOfMeasure",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitsOfMeasure_CreatedByUserId",
                table: "UnitsOfMeasure",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuantityTypeMembers");

            migrationBuilder.DropTable(
                name: "UnitConversions");

            migrationBuilder.DropTable(
                name: "QuantityTypes");

            migrationBuilder.DropTable(
                name: "UnitsOfMeasure");
        }
    }
}
