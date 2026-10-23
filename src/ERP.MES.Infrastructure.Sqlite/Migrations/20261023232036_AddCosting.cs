using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemCostCalculations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    SequenceNo = table.Column<int>(type: "INTEGER", nullable: false),
                    Formula = table.Column<string>(type: "TEXT", nullable: false),
                    AcceptedBaseUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    ConsumedBaseUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    MaterialId = table.Column<long>(type: "INTEGER", nullable: true),
                    MaterialPriceId = table.Column<long>(type: "INTEGER", nullable: true),
                    PriceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    MachiningTotalRial = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaterialTotalRial = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalRial = table.Column<decimal>(type: "TEXT", nullable: false),
                    UnitCostRial = table.Column<decimal>(type: "TEXT", nullable: false),
                    CalculatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCostCalculations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemCostCalculations_MaterialPrices_MaterialPriceId",
                        column: x => x.MaterialPriceId,
                        principalTable: "MaterialPrices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostCalculations_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostCalculations_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostCalculations_Users_CalculatedByUserId",
                        column: x => x.CalculatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemCostLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    JobReportLineId = table.Column<long>(type: "INTEGER", nullable: true),
                    MachineId = table.Column<long>(type: "INTEGER", nullable: true),
                    RateId = table.Column<long>(type: "INTEGER", nullable: true),
                    WorkDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Minutes = table.Column<int>(type: "INTEGER", nullable: true),
                    WeightKg = table.Column<decimal>(type: "TEXT", nullable: true),
                    UnitPriceRial = table.Column<decimal>(type: "TEXT", nullable: false),
                    AmountRial = table.Column<decimal>(type: "TEXT", nullable: false),
                    CalculationId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCostLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemCostLines_DailyJobReportLines_JobReportLineId",
                        column: x => x.JobReportLineId,
                        principalTable: "DailyJobReportLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostLines_ItemCostCalculations_CalculationId",
                        column: x => x.CalculationId,
                        principalTable: "ItemCostCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostLines_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemPriceDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CalculationId = table.Column<long>(type: "INTEGER", nullable: false),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    Basis = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposedPriceRial = table.Column<decimal>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedPriceRial = table.Column<decimal>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    ContractReference = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPriceDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemPriceDecisions_ItemCostCalculations_CalculationId",
                        column: x => x.CalculationId,
                        principalTable: "ItemCostCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemPriceDecisions_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemPriceDecisions_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemPriceDecisions_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostCalculations_CalculatedByUserId",
                table: "ItemCostCalculations",
                column: "CalculatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostCalculations_MaterialId",
                table: "ItemCostCalculations",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostCalculations_MaterialPriceId",
                table: "ItemCostCalculations",
                column: "MaterialPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostCalculations_RequestItemId_SequenceNo",
                table: "ItemCostCalculations",
                columns: new[] { "RequestItemId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostLines_CalculationId",
                table: "ItemCostLines",
                column: "CalculationId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostLines_JobReportLineId",
                table: "ItemCostLines",
                column: "JobReportLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostLines_MachineId",
                table: "ItemCostLines",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemPriceDecisions_ApprovedByUserId",
                table: "ItemPriceDecisions",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemPriceDecisions_CalculationId",
                table: "ItemPriceDecisions",
                column: "CalculationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemPriceDecisions_RequestItemId",
                table: "ItemPriceDecisions",
                column: "RequestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemPriceDecisions_SubmittedByUserId",
                table: "ItemPriceDecisions",
                column: "SubmittedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemCostLines");

            migrationBuilder.DropTable(
                name: "ItemPriceDecisions");

            migrationBuilder.DropTable(
                name: "ItemCostCalculations");
        }
    }
}
