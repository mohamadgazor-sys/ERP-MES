using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ItemDrawings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemDrawings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupersededByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemDrawings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemDrawings_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemDrawings_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemDrawings_Users_SupersededByUserId",
                        column: x => x.SupersededByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemDrawings_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemDrawings_CurrentPerItem",
                table: "ItemDrawings",
                column: "RequestItemId",
                unique: true,
                filter: "[SupersededAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ItemDrawings_DocumentId",
                table: "ItemDrawings",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemDrawings_RequestItemId_Revision",
                table: "ItemDrawings",
                columns: new[] { "RequestItemId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemDrawings_SupersededByUserId",
                table: "ItemDrawings",
                column: "SupersededByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemDrawings_UploadedByUserId",
                table: "ItemDrawings",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemDrawings");
        }
    }
}
