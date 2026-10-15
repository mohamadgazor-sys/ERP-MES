using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    DepartmentId = table.Column<long>(type: "INTEGER", nullable: false),
                    RoleId = table.Column<long>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LockoutEndUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastLoginAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PasswordSalt = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PasswordAlgorithm = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHashVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    PasswordWorkFactor = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: true),
                    MachineName = table.Column<string>(type: "TEXT", nullable: false),
                    OsUserName = table.Column<string>(type: "TEXT", nullable: false),
                    DatabaseLogin = table.Column<string>(type: "TEXT", nullable: true),
                    ActionCode = table.Column<string>(type: "TEXT", nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLog_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SystemSetup",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    BootstrapTokenHash = table.Column<byte[]>(type: "BLOB", nullable: true),
                    BootstrapTokenExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BootstrapFailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    InitializedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AdminChangeStamp = table.Column<long>(type: "INTEGER", nullable: false),
                    RecoveryTokenHash = table.Column<byte[]>(type: "BLOB", nullable: true),
                    RecoveryTokenExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RecoveryFailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    RecoveryTargetUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSetup", x => x.Id);
                    table.CheckConstraint("CK_SystemSetup_SingleRow", "Id = 1");
                    table.ForeignKey(
                        name: "FK_SystemSetup_Users_RecoveryTargetUserId",
                        column: x => x.RecoveryTargetUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Departments",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1L, "CENTRAL", "مرکزی" },
                    { 2L, "MANAGEMENT", "مدیریت" },
                    { 3L, "PLANNING", "برنامه‌ریزی" },
                    { 4L, "DRAWING", "نقشه‌کشی" },
                    { 5L, "WAREHOUSE", "انبار" },
                    { 6L, "PRODUCTION", "تولید" },
                    { 7L, "QC", "کنترل کیفیت" },
                    { 8L, "MAINTENANCE_PM", "نگهداری و تعمیرات" },
                    { 9L, "SYSTEM", "سیستم" }
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1L, "ADMIN", "مدیر سیستم" },
                    { 2L, "MANAGER", "مدیر" },
                    { 3L, "STAFF", "کارمند" }
                });

            migrationBuilder.InsertData(
                table: "SystemSetup",
                columns: new[] { "Id", "AdminChangeStamp", "BootstrapFailedAttempts", "BootstrapTokenExpiresAtUtc", "BootstrapTokenHash", "InitializedAtUtc", "RecoveryFailedAttempts", "RecoveryTargetUserId", "RecoveryTokenExpiresAtUtc", "RecoveryTokenHash", "State", "Version" },
                values: new object[] { 1L, 0L, 0, null, null, null, 0, null, null, null, "NotProvisioned", 1L });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_UserId",
                table: "AuditLog",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Code",
                table: "Departments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Code",
                table: "Roles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemSetup_RecoveryTargetUserId",
                table: "SystemSetup",
                column: "RecoveryTargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DepartmentId",
                table: "Users",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "SystemSetup");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
