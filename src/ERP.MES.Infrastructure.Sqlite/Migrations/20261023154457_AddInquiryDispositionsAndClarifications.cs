using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddInquiryDispositionsAndClarifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DrawingClarifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<long>(type: "INTEGER", nullable: false),
                    Question = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AskedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    AskedByDepartmentCode = table.Column<string>(type: "TEXT", nullable: false),
                    AskedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Answer = table.Column<string>(type: "TEXT", nullable: true),
                    AnsweredByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    AnsweredByDepartmentCode = table.Column<string>(type: "TEXT", nullable: true),
                    AnsweredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CloseNote = table.Column<string>(type: "TEXT", nullable: true),
                    ClosedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawingClarifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawingClarifications_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DrawingClarifications_Users_AnsweredByUserId",
                        column: x => x.AnsweredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DrawingClarifications_Users_AskedByUserId",
                        column: x => x.AskedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DrawingClarifications_Users_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialInquiryDispositions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResponseId = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    NewRoundInquiryId = table.Column<long>(type: "INTEGER", nullable: true),
                    DecidedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    DecidedByDepartmentCode = table.Column<string>(type: "TEXT", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LineId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialInquiryDispositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiryDispositions_RawMaterialInquiries_NewRoundInquiryId",
                        column: x => x.NewRoundInquiryId,
                        principalTable: "RawMaterialInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiryDispositions_RawMaterialInquiryLines_LineId",
                        column: x => x.LineId,
                        principalTable: "RawMaterialInquiryLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiryDispositions_RawMaterialInquiryResponses_ResponseId",
                        column: x => x.ResponseId,
                        principalTable: "RawMaterialInquiryResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiryDispositions_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialInquirySupplyRoutes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResponseId = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalPurchase = table.Column<bool>(type: "INTEGER", nullable: false),
                    DecidedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    DecidedByDepartmentCode = table.Column<string>(type: "TEXT", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LineId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialInquirySupplyRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquirySupplyRoutes_RawMaterialInquiryLines_LineId",
                        column: x => x.LineId,
                        principalTable: "RawMaterialInquiryLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquirySupplyRoutes_RawMaterialInquiryResponses_ResponseId",
                        column: x => x.ResponseId,
                        principalTable: "RawMaterialInquiryResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquirySupplyRoutes_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DrawingClarifications_AnsweredByUserId",
                table: "DrawingClarifications",
                column: "AnsweredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingClarifications_AskedByUserId",
                table: "DrawingClarifications",
                column: "AskedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingClarifications_ClosedByUserId",
                table: "DrawingClarifications",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingClarifications_RequestId",
                table: "DrawingClarifications",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiryDispositions_DecidedByUserId",
                table: "RawMaterialInquiryDispositions",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiryDispositions_LineId",
                table: "RawMaterialInquiryDispositions",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiryDispositions_NewRoundInquiryId",
                table: "RawMaterialInquiryDispositions",
                column: "NewRoundInquiryId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiryDispositions_ResponseId",
                table: "RawMaterialInquiryDispositions",
                column: "ResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquirySupplyRoutes_DecidedByUserId",
                table: "RawMaterialInquirySupplyRoutes",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquirySupplyRoutes_LineId",
                table: "RawMaterialInquirySupplyRoutes",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquirySupplyRoutes_ResponseId",
                table: "RawMaterialInquirySupplyRoutes",
                column: "ResponseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DrawingClarifications");

            migrationBuilder.DropTable(
                name: "RawMaterialInquiryDispositions");

            migrationBuilder.DropTable(
                name: "RawMaterialInquirySupplyRoutes");
        }
    }
}
