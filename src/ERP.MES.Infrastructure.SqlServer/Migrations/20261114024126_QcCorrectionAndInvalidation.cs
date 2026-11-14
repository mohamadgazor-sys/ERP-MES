using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class QcCorrectionAndInvalidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WarehouseReceipts_RequestItemId",
                table: "WarehouseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_ItemFinishedSent_RequestItemId",
                table: "ItemFinishedSent");

            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                table: "WarehouseReceipts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "AffectedByQcCorrectionId",
                table: "ItemStages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                table: "ItemFinishedSent",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "QcCorrections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    StageNo = table.Column<int>(type: "int", nullable: false),
                    ReworkBaseUnits = table.Column<long>(type: "bigint", nullable: false),
                    ScrapBaseUnits = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AcceptedBeforeBaseUnits = table.Column<long>(type: "bigint", nullable: false),
                    ResultingStatus = table.Column<int>(type: "int", nullable: false),
                    AfterCustomerDispatch = table.Column<bool>(type: "bit", nullable: false),
                    CorrectedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CorrectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcCorrections_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcCorrections_Users_CorrectedByUserId",
                        column: x => x.CorrectedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QcCorrectionSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InspectionLineId = table.Column<long>(type: "bigint", nullable: false),
                    SupersededBaseUnits = table.Column<long>(type: "bigint", nullable: false),
                    CorrectionId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcCorrectionSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcCorrectionSources_QcCorrections_CorrectionId",
                        column: x => x.CorrectionId,
                        principalTable: "QcCorrections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcCorrectionSources_QcInspectionLines_InspectionLineId",
                        column: x => x.InspectionLineId,
                        principalTable: "QcInspectionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecordInvalidations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<long>(type: "bigint", nullable: false),
                    Effect = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ByUserId = table.Column<long>(type: "bigint", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QcCorrectionId = table.Column<long>(type: "bigint", nullable: true),
                    JobReportRevisionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordInvalidations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecordInvalidations_QcCorrections_QcCorrectionId",
                        column: x => x.QcCorrectionId,
                        principalTable: "QcCorrections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecordInvalidations_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecordInvalidations_Users_ByUserId",
                        column: x => x.ByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseReceipts_RequestItemId_Sequence",
                table: "WarehouseReceipts",
                columns: new[] { "RequestItemId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemStages_AffectedByQcCorrectionId",
                table: "ItemStages",
                column: "AffectedByQcCorrectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemFinishedSent_RequestItemId_Sequence",
                table: "ItemFinishedSent",
                columns: new[] { "RequestItemId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcCorrections_CorrectedByUserId",
                table: "QcCorrections",
                column: "CorrectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QcCorrections_RequestItemId_StageNo",
                table: "QcCorrections",
                columns: new[] { "RequestItemId", "StageNo" });

            migrationBuilder.CreateIndex(
                name: "IX_QcCorrectionSources_CorrectionId",
                table: "QcCorrectionSources",
                column: "CorrectionId");

            migrationBuilder.CreateIndex(
                name: "IX_QcCorrectionSources_InspectionLineId",
                table: "QcCorrectionSources",
                column: "InspectionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordInvalidations_ByUserId",
                table: "RecordInvalidations",
                column: "ByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordInvalidations_Kind_TargetId",
                table: "RecordInvalidations",
                columns: new[] { "Kind", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordInvalidations_QcCorrectionId",
                table: "RecordInvalidations",
                column: "QcCorrectionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordInvalidations_RequestItemId",
                table: "RecordInvalidations",
                column: "RequestItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemStages_QcCorrections_AffectedByQcCorrectionId",
                table: "ItemStages",
                column: "AffectedByQcCorrectionId",
                principalTable: "QcCorrections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemStages_QcCorrections_AffectedByQcCorrectionId",
                table: "ItemStages");

            migrationBuilder.DropTable(
                name: "QcCorrectionSources");

            migrationBuilder.DropTable(
                name: "RecordInvalidations");

            migrationBuilder.DropTable(
                name: "QcCorrections");

            migrationBuilder.DropIndex(
                name: "IX_WarehouseReceipts_RequestItemId_Sequence",
                table: "WarehouseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_ItemStages_AffectedByQcCorrectionId",
                table: "ItemStages");

            migrationBuilder.DropIndex(
                name: "IX_ItemFinishedSent_RequestItemId_Sequence",
                table: "ItemFinishedSent");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "WarehouseReceipts");

            migrationBuilder.DropColumn(
                name: "AffectedByQcCorrectionId",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "ItemFinishedSent");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseReceipts_RequestItemId",
                table: "WarehouseReceipts",
                column: "RequestItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemFinishedSent_RequestItemId",
                table: "ItemFinishedSent",
                column: "RequestItemId",
                unique: true);
        }
    }
}
