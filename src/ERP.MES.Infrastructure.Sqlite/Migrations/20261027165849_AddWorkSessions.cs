using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PauseReasons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DefaultScope = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiresNote = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PauseReasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PauseReasons_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    StageNo = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    MachineId = table.Column<long>(type: "INTEGER", nullable: false),
                    OperatorId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PredecessorSessionId = table.Column<long>(type: "INTEGER", nullable: true),
                    QualificationWarning = table.Column<bool>(type: "INTEGER", nullable: false),
                    OutsideShiftReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DispatchedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    DispatchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishKind = table.Column<int>(type: "INTEGER", nullable: true),
                    FinishNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TimeApprovedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    TimeApprovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkSessions_ItemStages_RequestItemId_StageNo",
                        columns: x => new { x.RequestItemId, x.StageNo },
                        principalTable: "ItemStages",
                        principalColumns: new[] { "RequestItemId", "StageNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessions_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessions_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessions_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessions_Users_DispatchedByUserId",
                        column: x => x.DispatchedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessions_Users_FinishedByUserId",
                        column: x => x.FinishedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessions_Users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessions_Users_TimeApprovedByUserId",
                        column: x => x.TimeApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessions_WorkSessions_PredecessorSessionId",
                        column: x => x.PredecessorSessionId,
                        principalTable: "WorkSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutageEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReasonId = table.Column<long>(type: "INTEGER", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutageEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutageEvents_PauseReasons_ReasonId",
                        column: x => x.ReasonId,
                        principalTable: "PauseReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutageEvents_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutageEvents_Users_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutageEvents_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkSessionCorrections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Boundary = table.Column<int>(type: "INTEGER", nullable: false),
                    PauseIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    OldValueUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NewValueUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CorrectedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CorrectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SessionId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSessionCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkSessionCorrections_Users_CorrectedByUserId",
                        column: x => x.CorrectedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessionCorrections_WorkSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "WorkSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkSessionEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSessionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkSessionEvents_Users_ByUserId",
                        column: x => x.ByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessionEvents_WorkSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "WorkSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkSessionIntervals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MachineId = table.Column<long>(type: "INTEGER", nullable: false),
                    OperatorId = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StartedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    EndedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    EndedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    SessionId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSessionIntervals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkSessionIntervals_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessionIntervals_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessionIntervals_Users_EndedByUserId",
                        column: x => x.EndedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessionIntervals_Users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessionIntervals_WorkSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "WorkSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkSessionPauses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReasonId = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    OutageEventId = table.Column<long>(type: "INTEGER", nullable: true),
                    PausedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    PausedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResumedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResumedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    SessionId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSessionPauses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkSessionPauses_OutageEvents_OutageEventId",
                        column: x => x.OutageEventId,
                        principalTable: "OutageEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessionPauses_PauseReasons_ReasonId",
                        column: x => x.ReasonId,
                        principalTable: "PauseReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessionPauses_Users_PausedByUserId",
                        column: x => x.PausedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessionPauses_Users_ResumedByUserId",
                        column: x => x.ResumedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSessionPauses_WorkSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "WorkSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PauseReasons",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "CreatedByUserId", "DefaultScope", "IsActive", "IsSystem", "Name", "RequiresNote", "Version" },
                values: new object[,]
                {
                    { 1L, "POWER_OUTAGE", new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), null, 4, true, true, "قطع برق", false, 1L },
                    { 2L, "PLANNED_MAINTENANCE", new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), null, 2, true, true, "تعمیر و نگهداری پیشگیرانهٔ برنامه‌ریزی‌شده", false, 1L },
                    { 3L, "MACHINE_BREAKDOWN", new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), null, 2, true, true, "خرابی برنامه‌ریزی‌نشدهٔ ماشین", false, 1L },
                    { 4L, "OPERATOR_ABSENCE_HANDOVER", new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), null, 3, true, true, "غیبت اپراتور یا تحویل شیفت", false, 1L },
                    { 5L, "MATERIAL_UNAVAILABLE", new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), null, 1, true, true, "نبود مواد", false, 1L },
                    { 6L, "WAITING_ASSISTANCE", new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), null, 1, true, true, "انتظار برای سرپرست یا پشتیبانی فنی", false, 1L },
                    { 7L, "OTHER", new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), null, 1, true, true, "سایر (توضیح الزامی)", true, 1L }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutageEvents_ClosedByUserId",
                table: "OutageEvents",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutageEvents_ProductionUnitId",
                table: "OutageEvents",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_OutageEvents_ReasonId",
                table: "OutageEvents",
                column: "ReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_OutageEvents_RecordedByUserId",
                table: "OutageEvents",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PauseReasons_Code",
                table: "PauseReasons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PauseReasons_CreatedByUserId",
                table: "PauseReasons",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionCorrections_CorrectedByUserId",
                table: "WorkSessionCorrections",
                column: "CorrectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionCorrections_SessionId",
                table: "WorkSessionCorrections",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionEvents_ByUserId",
                table: "WorkSessionEvents",
                column: "ByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionEvents_SessionId",
                table: "WorkSessionEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionIntervals_EndedByUserId",
                table: "WorkSessionIntervals",
                column: "EndedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionIntervals_MachineId_StartedAtUtc",
                table: "WorkSessionIntervals",
                columns: new[] { "MachineId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionIntervals_OpenMachine",
                table: "WorkSessionIntervals",
                column: "MachineId",
                unique: true,
                filter: "[EndedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionIntervals_OpenOperator",
                table: "WorkSessionIntervals",
                column: "OperatorId",
                unique: true,
                filter: "[EndedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionIntervals_SessionId",
                table: "WorkSessionIntervals",
                column: "SessionId",
                unique: true,
                filter: "[EndedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionIntervals_StartedByUserId",
                table: "WorkSessionIntervals",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionPauses_OpenPerSession",
                table: "WorkSessionPauses",
                column: "SessionId",
                unique: true,
                filter: "[ResumedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionPauses_OutageEventId",
                table: "WorkSessionPauses",
                column: "OutageEventId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionPauses_PausedByUserId",
                table: "WorkSessionPauses",
                column: "PausedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionPauses_ReasonId",
                table: "WorkSessionPauses",
                column: "ReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessionPauses_ResumedByUserId",
                table: "WorkSessionPauses",
                column: "ResumedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessions_DispatchedByUserId",
                table: "WorkSessions",
                column: "DispatchedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessions_FinishedByUserId",
                table: "WorkSessions",
                column: "FinishedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessions_MachineId",
                table: "WorkSessions",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessions_OperatorId",
                table: "WorkSessions",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessions_PredecessorSessionId",
                table: "WorkSessions",
                column: "PredecessorSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessions_ProductionUnitId_Status",
                table: "WorkSessions",
                columns: new[] { "ProductionUnitId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessions_RequestItemId_StageNo",
                table: "WorkSessions",
                columns: new[] { "RequestItemId", "StageNo" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessions_StartedByUserId",
                table: "WorkSessions",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSessions_TimeApprovedByUserId",
                table: "WorkSessions",
                column: "TimeApprovedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkSessionCorrections");

            migrationBuilder.DropTable(
                name: "WorkSessionEvents");

            migrationBuilder.DropTable(
                name: "WorkSessionIntervals");

            migrationBuilder.DropTable(
                name: "WorkSessionPauses");

            migrationBuilder.DropTable(
                name: "OutageEvents");

            migrationBuilder.DropTable(
                name: "WorkSessions");

            migrationBuilder.DropTable(
                name: "PauseReasons");
        }
    }
}
