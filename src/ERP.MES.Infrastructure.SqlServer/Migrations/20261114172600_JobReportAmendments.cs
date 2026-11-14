using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class JobReportAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyJobReports_OperatorId_WorkDate_ShiftTemplateId",
                table: "DailyJobReports");

            migrationBuilder.AddColumn<int>(
                name: "AmendmentNo",
                table: "DailyJobReports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AmendmentOpenedAtUtc",
                table: "DailyJobReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AmendmentOpenedByUserId",
                table: "DailyJobReports",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AmendmentReason",
                table: "DailyJobReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PreviousRevisionReportId",
                table: "DailyJobReports",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordInvalidations_JobReportRevisionId",
                table: "RecordInvalidations",
                column: "JobReportRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_AmendmentOpenedByUserId",
                table: "DailyJobReports",
                column: "AmendmentOpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_OperatorId_WorkDate_ShiftTemplateId_AmendmentNo",
                table: "DailyJobReports",
                columns: new[] { "OperatorId", "WorkDate", "ShiftTemplateId", "AmendmentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_PreviousRevisionReportId",
                table: "DailyJobReports",
                column: "PreviousRevisionReportId");

            migrationBuilder.AddForeignKey(
                name: "FK_DailyJobReports_DailyJobReports_PreviousRevisionReportId",
                table: "DailyJobReports",
                column: "PreviousRevisionReportId",
                principalTable: "DailyJobReports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DailyJobReports_Users_AmendmentOpenedByUserId",
                table: "DailyJobReports",
                column: "AmendmentOpenedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecordInvalidations_DailyJobReports_JobReportRevisionId",
                table: "RecordInvalidations",
                column: "JobReportRevisionId",
                principalTable: "DailyJobReports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyJobReports_DailyJobReports_PreviousRevisionReportId",
                table: "DailyJobReports");

            migrationBuilder.DropForeignKey(
                name: "FK_DailyJobReports_Users_AmendmentOpenedByUserId",
                table: "DailyJobReports");

            migrationBuilder.DropForeignKey(
                name: "FK_RecordInvalidations_DailyJobReports_JobReportRevisionId",
                table: "RecordInvalidations");

            migrationBuilder.DropIndex(
                name: "IX_RecordInvalidations_JobReportRevisionId",
                table: "RecordInvalidations");

            migrationBuilder.DropIndex(
                name: "IX_DailyJobReports_AmendmentOpenedByUserId",
                table: "DailyJobReports");

            migrationBuilder.DropIndex(
                name: "IX_DailyJobReports_OperatorId_WorkDate_ShiftTemplateId_AmendmentNo",
                table: "DailyJobReports");

            migrationBuilder.DropIndex(
                name: "IX_DailyJobReports_PreviousRevisionReportId",
                table: "DailyJobReports");

            migrationBuilder.DropColumn(
                name: "AmendmentNo",
                table: "DailyJobReports");

            migrationBuilder.DropColumn(
                name: "AmendmentOpenedAtUtc",
                table: "DailyJobReports");

            migrationBuilder.DropColumn(
                name: "AmendmentOpenedByUserId",
                table: "DailyJobReports");

            migrationBuilder.DropColumn(
                name: "AmendmentReason",
                table: "DailyJobReports");

            migrationBuilder.DropColumn(
                name: "PreviousRevisionReportId",
                table: "DailyJobReports");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_OperatorId_WorkDate_ShiftTemplateId",
                table: "DailyJobReports",
                columns: new[] { "OperatorId", "WorkDate", "ShiftTemplateId" },
                unique: true);
        }
    }
}
