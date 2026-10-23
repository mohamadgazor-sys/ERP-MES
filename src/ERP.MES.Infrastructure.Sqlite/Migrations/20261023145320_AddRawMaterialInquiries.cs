using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRawMaterialInquiries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RawMaterialInquiries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<long>(type: "INTEGER", nullable: false),
                    RoundNo = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialInquiries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiries_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiries_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialInquiryHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    FromStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    ToStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: true),
                    Answer = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    ActionByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ActionByDepartmentCode = table.Column<string>(type: "TEXT", nullable: false),
                    ActionAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InquiryId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialInquiryHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiryHistory_RawMaterialInquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "RawMaterialInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiryHistory_Users_ActionByUserId",
                        column: x => x.ActionByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialInquiryLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterialGrade = table.Column<string>(type: "TEXT", nullable: false),
                    MaterialForm = table.Column<string>(type: "TEXT", nullable: false),
                    DiameterMm = table.Column<decimal>(type: "TEXT", nullable: true),
                    LengthMm = table.Column<decimal>(type: "TEXT", nullable: true),
                    OtherDimension = table.Column<string>(type: "TEXT", nullable: true),
                    RequiredQuantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    QuantityUnitId = table.Column<long>(type: "INTEGER", nullable: false),
                    StandardOrSpec = table.Column<string>(type: "TEXT", nullable: true),
                    Remarks = table.Column<string>(type: "TEXT", nullable: true),
                    InquiryId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialInquiryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiryLines_RawMaterialInquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "RawMaterialInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiryLines_UnitsOfMeasure_QuantityUnitId",
                        column: x => x.QuantityUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialInquiryResponses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Answer = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    RespondedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    RespondedByDepartmentCode = table.Column<string>(type: "TEXT", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LineId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialInquiryResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiryResponses_RawMaterialInquiryLines_LineId",
                        column: x => x.LineId,
                        principalTable: "RawMaterialInquiryLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawMaterialInquiryResponses_Users_RespondedByUserId",
                        column: x => x.RespondedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiries_CreatedByUserId",
                table: "RawMaterialInquiries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiries_RequestId_RoundNo",
                table: "RawMaterialInquiries",
                columns: new[] { "RequestId", "RoundNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiryHistory_ActionByUserId",
                table: "RawMaterialInquiryHistory",
                column: "ActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiryHistory_InquiryId",
                table: "RawMaterialInquiryHistory",
                column: "InquiryId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiryLines_InquiryId_LineNo",
                table: "RawMaterialInquiryLines",
                columns: new[] { "InquiryId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiryLines_QuantityUnitId",
                table: "RawMaterialInquiryLines",
                column: "QuantityUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiryResponses_LineId",
                table: "RawMaterialInquiryResponses",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiryResponses_RespondedByUserId",
                table: "RawMaterialInquiryResponses",
                column: "RespondedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawMaterialInquiryHistory");

            migrationBuilder.DropTable(
                name: "RawMaterialInquiryResponses");

            migrationBuilder.DropTable(
                name: "RawMaterialInquiryLines");

            migrationBuilder.DropTable(
                name: "RawMaterialInquiries");
        }
    }
}
