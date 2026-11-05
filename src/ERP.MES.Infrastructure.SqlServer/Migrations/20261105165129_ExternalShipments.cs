using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ExternalShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ExternalDispatchedBaseUnits",
                table: "ItemStages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ExternalReturnedBaseUnits",
                table: "ItemStages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ExternalShipments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    StageNo = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    DispatchId = table.Column<long>(type: "bigint", nullable: true),
                    BaseUnits = table.Column<long>(type: "bigint", nullable: false),
                    PieceDescription = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RecordedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CancelledByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalShipments", x => x.Id);
                    table.CheckConstraint("CK_ExternalShipments_BaseUnits", "\"BaseUnits\" > 0");
                    table.ForeignKey(
                        name: "FK_ExternalShipments_ExternalShipments_DispatchId",
                        column: x => x.DispatchId,
                        principalTable: "ExternalShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalShipments_ItemStages_RequestItemId_StageNo",
                        columns: x => new { x.RequestItemId, x.StageNo },
                        principalTable: "ItemStages",
                        principalColumns: new[] { "RequestItemId", "StageNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalShipments_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalShipments_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalShipments_CancelledByUserId",
                table: "ExternalShipments",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalShipments_DispatchId",
                table: "ExternalShipments",
                column: "DispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalShipments_RecordedByUserId",
                table: "ExternalShipments",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalShipments_RequestItemId_StageNo",
                table: "ExternalShipments",
                columns: new[] { "RequestItemId", "StageNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalShipments");

            migrationBuilder.DropColumn(
                name: "ExternalDispatchedBaseUnits",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "ExternalReturnedBaseUnits",
                table: "ItemStages");
        }
    }
}
