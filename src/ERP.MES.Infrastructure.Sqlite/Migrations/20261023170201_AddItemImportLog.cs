using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddItemImportLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemImports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<long>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", nullable: false),
                    ImportedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemImports_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemImports_Users_ImportedByUserId",
                        column: x => x.ImportedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemImportRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SheetRowNo = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemNo = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemImportId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemImportRows_ItemImports_ItemImportId",
                        column: x => x.ItemImportId,
                        principalTable: "ItemImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemImportRows_ItemImportId",
                table: "ItemImportRows",
                column: "ItemImportId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemImports_ImportedByUserId",
                table: "ItemImports",
                column: "ImportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemImports_RequestId",
                table: "ItemImports",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemImportRows");

            migrationBuilder.DropTable(
                name: "ItemImports");
        }
    }
}
