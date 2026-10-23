using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableSinceUtc",
                table: "ItemStages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanningPosition",
                table: "ItemStages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StopReason",
                table: "ItemStages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StoppedAtUtc",
                table: "ItemStages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StoppedByUserId",
                table: "ItemStages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QueueOverrideHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OperationId = table.Column<string>(type: "TEXT", nullable: false),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    StageNo = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    OldRank = table.Column<int>(type: "INTEGER", nullable: true),
                    NewRank = table.Column<int>(type: "INTEGER", nullable: true),
                    OldOrder = table.Column<int>(type: "INTEGER", nullable: true),
                    NewOrder = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueOverrideHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueOverrideHistory_ItemStages_RequestItemId_StageNo",
                        columns: x => new { x.RequestItemId, x.StageNo },
                        principalTable: "ItemStages",
                        principalColumns: new[] { "RequestItemId", "StageNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueOverrideHistory_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueOverrideHistory_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QueueOverrides",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    StageNo = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    SetByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    SetAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NeedsReview = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReviewNote = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewFlaggedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueOverrides_ItemStages_RequestItemId_StageNo",
                        columns: x => new { x.RequestItemId, x.StageNo },
                        principalTable: "ItemStages",
                        principalColumns: new[] { "RequestItemId", "StageNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueOverrides_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueOverrides_Users_SetByUserId",
                        column: x => x.SetByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserUnitDesignations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    AssignedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserUnitDesignations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserUnitDesignations_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserUnitDesignations_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserUnitDesignations_Users_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserUnitDesignations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemStages_StoppedByUserId",
                table: "ItemStages",
                column: "StoppedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueOverrideHistory_OperationId",
                table: "QueueOverrideHistory",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueOverrideHistory_ProductionUnitId",
                table: "QueueOverrideHistory",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueOverrideHistory_RequestItemId_StageNo",
                table: "QueueOverrideHistory",
                columns: new[] { "RequestItemId", "StageNo" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueOverrideHistory_UserId",
                table: "QueueOverrideHistory",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueOverrides_ProductionUnitId_IsActive",
                table: "QueueOverrides",
                columns: new[] { "ProductionUnitId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueOverrides_RequestItemId_StageNo",
                table: "QueueOverrides",
                columns: new[] { "RequestItemId", "StageNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueOverrides_SetByUserId",
                table: "QueueOverrides",
                column: "SetByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserUnitDesignations_AssignedByUserId",
                table: "UserUnitDesignations",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserUnitDesignations_ProductionUnitId",
                table: "UserUnitDesignations",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_UserUnitDesignations_RevokedByUserId",
                table: "UserUnitDesignations",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserUnitDesignations_UserId_ProductionUnitId",
                table: "UserUnitDesignations",
                columns: new[] { "UserId", "ProductionUnitId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ItemStages_Users_StoppedByUserId",
                table: "ItemStages",
                column: "StoppedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemStages_Users_StoppedByUserId",
                table: "ItemStages");

            migrationBuilder.DropTable(
                name: "QueueOverrideHistory");

            migrationBuilder.DropTable(
                name: "QueueOverrides");

            migrationBuilder.DropTable(
                name: "UserUnitDesignations");

            migrationBuilder.DropIndex(
                name: "IX_ItemStages_StoppedByUserId",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "AvailableSinceUtc",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "PlanningPosition",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "StopReason",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "StoppedAtUtc",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "StoppedByUserId",
                table: "ItemStages");
        }
    }
}
