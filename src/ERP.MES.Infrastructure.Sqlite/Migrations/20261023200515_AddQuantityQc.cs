using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddQuantityQc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BaseUnitsPerUnit",
                table: "RequestItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "AcceptedBaseUnits",
                table: "ItemStages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "AwaitingQcBaseUnits",
                table: "ItemStages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "InputBaseUnits",
                table: "ItemStages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReworkBaseUnits",
                table: "ItemStages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ScrapBaseUnits",
                table: "ItemStages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "QcInspectionLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    StageNo = table.Column<int>(type: "INTEGER", nullable: false),
                    InspectionId = table.Column<string>(type: "TEXT", nullable: false),
                    Disposition = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    DecidedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcInspectionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcInspectionLines_ItemStages_RequestItemId_StageNo",
                        columns: x => new { x.RequestItemId, x.StageNo },
                        principalTable: "ItemStages",
                        principalColumns: new[] { "RequestItemId", "StageNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcInspectionLines_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShortfallDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    StageNo = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    ShortfallBaseUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    DecidedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortfallDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortfallDecisions_ItemStages_RequestItemId_StageNo",
                        columns: x => new { x.RequestItemId, x.StageNo },
                        principalTable: "ItemStages",
                        principalColumns: new[] { "RequestItemId", "StageNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShortfallDecisions_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QcInspectionLines_DecidedByUserId",
                table: "QcInspectionLines",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QcInspectionLines_RequestItemId_StageNo",
                table: "QcInspectionLines",
                columns: new[] { "RequestItemId", "StageNo" });

            migrationBuilder.CreateIndex(
                name: "IX_ShortfallDecisions_DecidedByUserId",
                table: "ShortfallDecisions",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortfallDecisions_RequestItemId_StageNo",
                table: "ShortfallDecisions",
                columns: new[] { "RequestItemId", "StageNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QcInspectionLines");

            migrationBuilder.DropTable(
                name: "ShortfallDecisions");

            migrationBuilder.DropColumn(
                name: "BaseUnitsPerUnit",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "AcceptedBaseUnits",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "AwaitingQcBaseUnits",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "InputBaseUnits",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "ReworkBaseUnits",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "ScrapBaseUnits",
                table: "ItemStages");
        }
    }
}
