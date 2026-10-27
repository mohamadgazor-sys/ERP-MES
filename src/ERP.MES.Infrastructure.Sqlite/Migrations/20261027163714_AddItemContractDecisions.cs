using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddItemContractDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemContractDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<long>(type: "INTEGER", nullable: false),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    Requirement = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    DecidedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemContractDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemContractDecisions_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemContractDecisions_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemContractDecisions_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemContractDecisions_DecidedByUserId",
                table: "ItemContractDecisions",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemContractDecisions_RequestId",
                table: "ItemContractDecisions",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemContractDecisions_RequestItemId",
                table: "ItemContractDecisions",
                column: "RequestItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemContractDecisions");
        }
    }
}
