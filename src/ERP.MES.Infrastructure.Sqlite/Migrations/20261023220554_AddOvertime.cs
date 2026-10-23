using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddOvertime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OvertimeAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OperatorId = table.Column<long>(type: "INTEGER", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    MachineId = table.Column<long>(type: "INTEGER", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartLocal = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndLocal = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ProposedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExceedsLimits = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlanningUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    PlanningAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ManagementUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    ManagementAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecisionNote = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OvertimeAssignments_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvertimeAssignments_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvertimeAssignments_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvertimeAssignments_Users_ManagementUserId",
                        column: x => x.ManagementUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvertimeAssignments_Users_PlanningUserId",
                        column: x => x.PlanningUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvertimeAssignments_Users_ProposedByUserId",
                        column: x => x.ProposedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OvertimeLimits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxMinutesPerDay = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxMinutesPerWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeLimits", x => x.Id);
                    table.CheckConstraint("CK_OvertimeLimits_Single", "\"Id\" = 1");
                    table.ForeignKey(
                        name: "FK_OvertimeLimits_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeAssignments_MachineId_WorkDate",
                table: "OvertimeAssignments",
                columns: new[] { "MachineId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeAssignments_ManagementUserId",
                table: "OvertimeAssignments",
                column: "ManagementUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeAssignments_OperatorId_WorkDate",
                table: "OvertimeAssignments",
                columns: new[] { "OperatorId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeAssignments_PlanningUserId",
                table: "OvertimeAssignments",
                column: "PlanningUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeAssignments_ProductionUnitId",
                table: "OvertimeAssignments",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeAssignments_ProposedByUserId",
                table: "OvertimeAssignments",
                column: "ProposedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeLimits_ChangedByUserId",
                table: "OvertimeLimits",
                column: "ChangedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OvertimeAssignments");

            migrationBuilder.DropTable(
                name: "OvertimeLimits");
        }
    }
}
