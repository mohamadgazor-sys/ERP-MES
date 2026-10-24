using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerDispatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    DispatchDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DocumentReference = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    RecordedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDispatches_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDispatches_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseReceipts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedBaseUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    ReceivedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseReceipts_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseReceipts_Users_ReceivedByUserId",
                        column: x => x.ReceivedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemFinishedSent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceiptId = table.Column<long>(type: "INTEGER", nullable: false),
                    PriceDecisionId = table.Column<long>(type: "INTEGER", nullable: false),
                    ApprovedPriceRial = table.Column<decimal>(type: "TEXT", nullable: false),
                    MarkedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    MarkedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemFinishedSent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemFinishedSent_ItemPriceDecisions_PriceDecisionId",
                        column: x => x.PriceDecisionId,
                        principalTable: "ItemPriceDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemFinishedSent_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemFinishedSent_Users_MarkedByUserId",
                        column: x => x.MarkedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemFinishedSent_WarehouseReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "WarehouseReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDispatches_RecordedByUserId",
                table: "CustomerDispatches",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDispatches_RequestItemId",
                table: "CustomerDispatches",
                column: "RequestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemFinishedSent_MarkedByUserId",
                table: "ItemFinishedSent",
                column: "MarkedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemFinishedSent_PriceDecisionId",
                table: "ItemFinishedSent",
                column: "PriceDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemFinishedSent_ReceiptId",
                table: "ItemFinishedSent",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemFinishedSent_RequestItemId",
                table: "ItemFinishedSent",
                column: "RequestItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseReceipts_ReceivedByUserId",
                table: "WarehouseReceipts",
                column: "ReceivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseReceipts_RequestItemId",
                table: "WarehouseReceipts",
                column: "RequestItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerDispatches");

            migrationBuilder.DropTable(
                name: "ItemFinishedSent");

            migrationBuilder.DropTable(
                name: "WarehouseReceipts");
        }
    }
}
