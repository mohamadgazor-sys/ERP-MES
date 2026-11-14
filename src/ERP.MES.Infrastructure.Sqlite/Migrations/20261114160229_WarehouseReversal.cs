using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class WarehouseReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "WarehouseReversalId",
                table: "RecordInvalidations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WarehouseReversals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetKind = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetId = table.Column<long>(type: "INTEGER", nullable: false),
                    Nature = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposalReason = table.Column<string>(type: "TEXT", nullable: false),
                    ProposedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ProposedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseReversals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseReversals_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseReversals_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseReversals_Users_ProposedByUserId",
                        column: x => x.ProposedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecordInvalidations_WarehouseReversalId",
                table: "RecordInvalidations",
                column: "WarehouseReversalId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseReversals_DecidedByUserId",
                table: "WarehouseReversals",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseReversals_ProposedByUserId",
                table: "WarehouseReversals",
                column: "ProposedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseReversals_RequestItemId",
                table: "WarehouseReversals",
                column: "RequestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseReversals_TargetKind_TargetId",
                table: "WarehouseReversals",
                columns: new[] { "TargetKind", "TargetId" });

            migrationBuilder.AddForeignKey(
                name: "FK_RecordInvalidations_WarehouseReversals_WarehouseReversalId",
                table: "RecordInvalidations",
                column: "WarehouseReversalId",
                principalTable: "WarehouseReversals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecordInvalidations_WarehouseReversals_WarehouseReversalId",
                table: "RecordInvalidations");

            migrationBuilder.DropTable(
                name: "WarehouseReversals");

            migrationBuilder.DropIndex(
                name: "IX_RecordInvalidations_WarehouseReversalId",
                table: "RecordInvalidations");

            migrationBuilder.DropColumn(
                name: "WarehouseReversalId",
                table: "RecordInvalidations");
        }
    }
}
