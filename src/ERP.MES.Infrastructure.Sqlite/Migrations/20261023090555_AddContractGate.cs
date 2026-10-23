using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddContractGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<long>(type: "INTEGER", nullable: false),
                    Requirement = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contracts_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    FromStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    ToStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionNo = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    ActionByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ActionByDepartmentCode = table.Column<string>(type: "TEXT", nullable: false),
                    ActionAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContractId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractHistory_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractHistory_Users_ActionByUserId",
                        column: x => x.ActionByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VersionNo = table.Column<int>(type: "INTEGER", nullable: false),
                    ContractNo = table.Column<string>(type: "TEXT", nullable: true),
                    ContractDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubmittedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Decision = table.Column<int>(type: "INTEGER", nullable: true),
                    DecisionNote = table.Column<string>(type: "TEXT", nullable: true),
                    DecidedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ContractId = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractVersions_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractVersions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractVersions_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractVersions_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractDocumentLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentId = table.Column<long>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    LinkedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ContractVersionId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractDocumentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractDocumentLinks_ContractVersions_ContractVersionId",
                        column: x => x.ContractVersionId,
                        principalTable: "ContractVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractDocumentLinks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractDocumentLinks_Users_LinkedByUserId",
                        column: x => x.LinkedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentLinks_ContractVersionId",
                table: "ContractDocumentLinks",
                column: "ContractVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentLinks_DocumentId",
                table: "ContractDocumentLinks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocumentLinks_LinkedByUserId",
                table: "ContractDocumentLinks",
                column: "LinkedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractHistory_ActionByUserId",
                table: "ContractHistory",
                column: "ActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractHistory_ContractId",
                table: "ContractHistory",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_RequestId",
                table: "Contracts",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_ContractId_VersionNo",
                table: "ContractVersions",
                columns: new[] { "ContractId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_CreatedByUserId",
                table: "ContractVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_DecidedByUserId",
                table: "ContractVersions",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractVersions_SubmittedByUserId",
                table: "ContractVersions",
                column: "SubmittedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractDocumentLinks");

            migrationBuilder.DropTable(
                name: "ContractHistory");

            migrationBuilder.DropTable(
                name: "ContractVersions");

            migrationBuilder.DropTable(
                name: "Contracts");
        }
    }
}
