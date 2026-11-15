using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class JobReportAmendmentCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "DailyJobReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "DailyJobReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CancelledByUserId",
                table: "DailyJobReports",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_CancelledByUserId",
                table: "DailyJobReports",
                column: "CancelledByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DailyJobReports_Users_CancelledByUserId",
                table: "DailyJobReports",
                column: "CancelledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyJobReports_Users_CancelledByUserId",
                table: "DailyJobReports");

            migrationBuilder.DropIndex(
                name: "IX_DailyJobReports_CancelledByUserId",
                table: "DailyJobReports");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "DailyJobReports");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "DailyJobReports");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "DailyJobReports");
        }
    }
}
