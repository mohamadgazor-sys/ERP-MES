using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperatorOffDays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OperatorId = table.Column<long>(type: "INTEGER", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    RecordedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorOffDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatorOffDays_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorOffDays_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    BreakStart = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    BreakEnd = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftTemplates_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyWorkdays",
                columns: table => new
                {
                    Day = table.Column<int>(type: "INTEGER", nullable: false),
                    IsWorkday = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyWorkdays", x => x.Day);
                });

            migrationBuilder.CreateTable(
                name: "CalendarExceptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "INTEGER", nullable: true),
                    ShiftTemplateId = table.Column<long>(type: "INTEGER", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarExceptions_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalendarExceptions_ShiftTemplates_ShiftTemplateId",
                        column: x => x.ShiftTemplateId,
                        principalTable: "ShiftTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalendarExceptions_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitShifts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductionUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    ShiftTemplateId = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxOperators = table.Column<int>(type: "INTEGER", nullable: false),
                    MinOperators = table.Column<int>(type: "INTEGER", nullable: false),
                    OverlapAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitShifts_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitShifts_ShiftTemplates_ShiftTemplateId",
                        column: x => x.ShiftTemplateId,
                        principalTable: "ShiftTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitShifts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkerSchedules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OperatorId = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    ShiftTemplateId = table.Column<long>(type: "INTEGER", nullable: false),
                    MachineId = table.Column<long>(type: "INTEGER", nullable: true),
                    HandoverMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    HandoverNote = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CancelReason = table.Column<string>(type: "TEXT", nullable: true),
                    CancelledByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_ShiftTemplates_ShiftTemplateId",
                        column: x => x.ShiftTemplateId,
                        principalTable: "ShiftTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "WeeklyWorkdays",
                columns: new[] { "Day", "IsWorkday", "UpdatedAtUtc", "Version" },
                values: new object[,]
                {
                    { 0, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 1, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 2, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 3, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 4, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 5, false, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 6, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarExceptions_Date",
                table: "CalendarExceptions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarExceptions_ProductionUnitId",
                table: "CalendarExceptions",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarExceptions_RecordedByUserId",
                table: "CalendarExceptions",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarExceptions_ShiftTemplateId",
                table: "CalendarExceptions",
                column: "ShiftTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorOffDays_OperatorId_FromDate",
                table: "OperatorOffDays",
                columns: new[] { "OperatorId", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OperatorOffDays_RecordedByUserId",
                table: "OperatorOffDays",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTemplates_Code",
                table: "ShiftTemplates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTemplates_CreatedByUserId",
                table: "ShiftTemplates",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitShifts_CreatedByUserId",
                table: "UnitShifts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitShifts_ProductionUnitId_ShiftTemplateId",
                table: "UnitShifts",
                columns: new[] { "ProductionUnitId", "ShiftTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitShifts_ShiftTemplateId",
                table: "UnitShifts",
                column: "ShiftTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_CancelledByUserId",
                table: "WorkerSchedules",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_CreatedByUserId",
                table: "WorkerSchedules",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_Date_ProductionUnitId",
                table: "WorkerSchedules",
                columns: new[] { "Date", "ProductionUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_MachineId",
                table: "WorkerSchedules",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_OperatorId_Date",
                table: "WorkerSchedules",
                columns: new[] { "OperatorId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_ProductionUnitId",
                table: "WorkerSchedules",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_ShiftTemplateId",
                table: "WorkerSchedules",
                column: "ShiftTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarExceptions");

            migrationBuilder.DropTable(
                name: "OperatorOffDays");

            migrationBuilder.DropTable(
                name: "UnitShifts");

            migrationBuilder.DropTable(
                name: "WeeklyWorkdays");

            migrationBuilder.DropTable(
                name: "WorkerSchedules");

            migrationBuilder.DropTable(
                name: "ShiftTemplates");
        }
    }
}
