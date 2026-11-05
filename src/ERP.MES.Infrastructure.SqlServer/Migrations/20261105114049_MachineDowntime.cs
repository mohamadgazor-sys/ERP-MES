using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class MachineDowntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MachineDowntimes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    StartLocal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndLocal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReportedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ReportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CancelledByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineDowntimes", x => x.Id);
                    table.CheckConstraint("CK_MachineDowntimes_EndAfterStart", "\"EndLocal\" > \"StartLocal\"");
                    table.ForeignKey(
                        name: "FK_MachineDowntimes_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineDowntimes_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineDowntimes_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineDowntimes_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineDowntimes_Users_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MachineDowntimes_CancelledByUserId",
                table: "MachineDowntimes",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineDowntimes_DecidedByUserId",
                table: "MachineDowntimes",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineDowntimes_MachineId_StartLocal",
                table: "MachineDowntimes",
                columns: new[] { "MachineId", "StartLocal" });

            migrationBuilder.CreateIndex(
                name: "IX_MachineDowntimes_ProductionUnitId",
                table: "MachineDowntimes",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineDowntimes_ReportedByUserId",
                table: "MachineDowntimes",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineDowntimes_StartLocal",
                table: "MachineDowntimes",
                column: "StartLocal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MachineDowntimes");
        }
    }
}
