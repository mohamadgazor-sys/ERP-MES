using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddJobReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSupervisor",
                table: "UserUnitDesignations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DailyJobReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OperatorId = table.Column<long>(type: "INTEGER", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    ShiftTemplateId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RevisionNo = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReturnReason = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedByPlanningFallback = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyJobReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyJobReports_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReports_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReports_ShiftTemplates_ShiftTemplateId",
                        column: x => x.ShiftTemplateId,
                        principalTable: "ShiftTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReports_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReports_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyJobReportEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    PlanningFallback = table.Column<bool>(type: "INTEGER", nullable: false),
                    DailyJobReportId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyJobReportEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyJobReportEvents_DailyJobReports_DailyJobReportId",
                        column: x => x.DailyJobReportId,
                        principalTable: "DailyJobReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReportEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyJobReportLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    MachineId = table.Column<long>(type: "INTEGER", nullable: false),
                    RequestId = table.Column<long>(type: "INTEGER", nullable: false),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    StageNo = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DowntimeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    DowntimeReason = table.Column<string>(type: "TEXT", nullable: true),
                    ProcessedBaseUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Flags = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyJobReportId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyJobReportLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyJobReportLines_DailyJobReports_DailyJobReportId",
                        column: x => x.DailyJobReportId,
                        principalTable: "DailyJobReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReportLines_ItemStages_RequestItemId_StageNo",
                        columns: x => new { x.RequestItemId, x.StageNo },
                        principalTable: "ItemStages",
                        principalColumns: new[] { "RequestItemId", "StageNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReportLines_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReportLines_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportEvents_DailyJobReportId",
                table: "DailyJobReportEvents",
                column: "DailyJobReportId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportEvents_UserId",
                table: "DailyJobReportEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportLines_DailyJobReportId",
                table: "DailyJobReportLines",
                column: "DailyJobReportId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportLines_MachineId_StartUtc",
                table: "DailyJobReportLines",
                columns: new[] { "MachineId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportLines_RequestId",
                table: "DailyJobReportLines",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportLines_RequestItemId_StageNo",
                table: "DailyJobReportLines",
                columns: new[] { "RequestItemId", "StageNo" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_CreatedByUserId",
                table: "DailyJobReports",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_OperatorId_WorkDate_ShiftTemplateId",
                table: "DailyJobReports",
                columns: new[] { "OperatorId", "WorkDate", "ShiftTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_ProductionUnitId",
                table: "DailyJobReports",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_ReviewedByUserId",
                table: "DailyJobReports",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_ShiftTemplateId",
                table: "DailyJobReports",
                column: "ShiftTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_WorkDate_ProductionUnitId",
                table: "DailyJobReports",
                columns: new[] { "WorkDate", "ProductionUnitId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyJobReportEvents");

            migrationBuilder.DropTable(
                name: "DailyJobReportLines");

            migrationBuilder.DropTable(
                name: "DailyJobReports");

            migrationBuilder.DropColumn(
                name: "IsSupervisor",
                table: "UserUnitDesignations");
        }
    }
}
