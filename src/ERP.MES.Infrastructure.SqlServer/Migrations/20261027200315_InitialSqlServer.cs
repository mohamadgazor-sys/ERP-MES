using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.MES.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyWorkdays",
                columns: table => new
                {
                    Day = table.Column<int>(type: "int", nullable: false),
                    IsWorkday = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyWorkdays", x => x.Day);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UsernameKey = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DepartmentId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    MachineName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OsUserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DatabaseLogin = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploadedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Form = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Materials_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Operators",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Operators_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Operators_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OvertimeLimits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    MaxMinutesPerDay = table.Column<int>(type: "int", nullable: false),
                    MaxMinutesPerWeek = table.Column<int>(type: "int", nullable: false),
                    ChangedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "PauseReasons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DefaultScope = table.Column<int>(type: "int", nullable: false),
                    RequiresNote = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                name: "ProductionUnits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionUnits_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    BreakStart = table.Column<TimeOnly>(type: "time", nullable: true),
                    BreakEnd = table.Column<TimeOnly>(type: "time", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftTemplates_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SystemSetup",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BootstrapTokenHash = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    BootstrapTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BootstrapFailedAttempts = table.Column<int>(type: "int", nullable: false),
                    InitializedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AdminChangeStamp = table.Column<long>(type: "bigint", nullable: false),
                    RecoveryTokenHash = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    RecoveryTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecoveryFailedAttempts = table.Column<int>(type: "int", nullable: false),
                    RecoveryTargetUserId = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "UnitsOfMeasure",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitsOfMeasure", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitsOfMeasure_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserCredentials",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PasswordHash = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PasswordSalt = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PasswordAlgorithm = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PasswordHashVersion = table.Column<int>(type: "int", nullable: false),
                    PasswordWorkFactor = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCredentials", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Requests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    RequestNo = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkType = table.Column<int>(type: "int", nullable: false),
                    RequestDate = table.Column<DateOnly>(type: "date", nullable: false),
                    JalaliRequestYear = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Requests_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Requests_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialPrices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaterialId = table.Column<long>(type: "bigint", nullable: false),
                    PricePerKgRial = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialPrices_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialPrices_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperatorOffDays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperatorId = table.Column<long>(type: "bigint", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecordedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorOffDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatorOffDays_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorOffDays_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Machines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Machines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Machines_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Machines_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutageEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReasonId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                name: "UserUnitDesignations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: false),
                    AssignedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    IsSupervisor = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "CalendarExceptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: true),
                    ShiftTemplateId = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarExceptions_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalendarExceptions_ShiftTemplates_ShiftTemplateId",
                        column: x => x.ShiftTemplateId,
                        principalTable: "ShiftTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalendarExceptions_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyJobReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperatorId = table.Column<long>(type: "bigint", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: false),
                    ShiftTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedByPlanningFallback = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyJobReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyJobReports_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReports_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReports_ShiftTemplates_ShiftTemplateId",
                        column: x => x.ShiftTemplateId,
                        principalTable: "ShiftTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReports_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReports_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitShifts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: false),
                    ShiftTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    MaxOperators = table.Column<int>(type: "int", nullable: false),
                    MinOperators = table.Column<int>(type: "int", nullable: false),
                    OverlapAllowed = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitShifts_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitShifts_ShiftTemplates_ShiftTemplateId",
                        column: x => x.ShiftTemplateId,
                        principalTable: "ShiftTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitShifts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuantityTypes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseUnitId = table.Column<long>(type: "bigint", nullable: false),
                    BaseUnitsPerType = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuantityTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuantityTypes_UnitsOfMeasure_BaseUnitId",
                        column: x => x.BaseUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuantityTypes_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitConversions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromUnitId = table.Column<long>(type: "bigint", nullable: false),
                    ToUnitId = table.Column<long>(type: "bigint", nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitConversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitConversions_UnitsOfMeasure_FromUnitId",
                        column: x => x.FromUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitConversions_UnitsOfMeasure_ToUnitId",
                        column: x => x.ToUnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitConversions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    Requirement = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                name: "DrawingClarifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AskedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    AskedByDepartmentCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AskedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnsweredByUserId = table.Column<long>(type: "bigint", nullable: true),
                    AnsweredByDepartmentCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnsweredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CloseNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClosedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                name: "ItemImports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemImports_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemImports_Users_ImportedByUserId",
                        column: x => x.ImportedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialInquiries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    RoundNo = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                name: "RequestDocumentLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LinkedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestDocumentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestDocumentLinks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestDocumentLinks_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestDocumentLinks_Users_LinkedByUserId",
                        column: x => x.LinkedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RequestWorkflowEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Action = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: false),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ActionByDepartmentCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestWorkflowEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestWorkflowEntries_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestWorkflowEntries_Users_ActionByUserId",
                        column: x => x.ActionByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MachineHourlyRates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<long>(type: "bigint", nullable: false),
                    RatePerHourRial = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineHourlyRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineHourlyRates_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MachineHourlyRates_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperatorMachineAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperatorId = table.Column<long>(type: "bigint", nullable: false),
                    MachineId = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssignedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorMachineAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatorMachineAssignments_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorMachineAssignments_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorMachineAssignments_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorMachineAssignments_Users_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperatorMachineQualifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperatorId = table.Column<long>(type: "bigint", nullable: false),
                    MachineId = table.Column<long>(type: "bigint", nullable: false),
                    QualifiedFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    QualifiedTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecordedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorMachineQualifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatorMachineQualifications_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorMachineQualifications_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorMachineQualifications_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OvertimeAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperatorId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: false),
                    MachineId = table.Column<long>(type: "bigint", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartLocal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndLocal = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProposedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ProposedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExceedsLimits = table.Column<bool>(type: "bit", nullable: false),
                    PlanningUserId = table.Column<long>(type: "bigint", nullable: true),
                    PlanningAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManagementUserId = table.Column<long>(type: "bigint", nullable: true),
                    ManagementAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                name: "WorkerSchedules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperatorId = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: false),
                    ShiftTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    MachineId = table.Column<long>(type: "bigint", nullable: true),
                    HandoverMinutes = table.Column<int>(type: "int", nullable: true),
                    HandoverNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CancelledByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_ShiftTemplates_ShiftTemplateId",
                        column: x => x.ShiftTemplateId,
                        principalTable: "ShiftTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerSchedules_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyJobReportEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlanningFallback = table.Column<bool>(type: "bit", nullable: false),
                    DailyJobReportId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyJobReportEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyJobReportEvents_DailyJobReports_DailyJobReportId",
                        column: x => x.DailyJobReportId,
                        principalTable: "DailyJobReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReportEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuantityTypeMembers",
                columns: table => new
                {
                    MemberNo = table.Column<int>(type: "int", nullable: false),
                    QuantityTypeId = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseUnits = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuantityTypeMembers", x => new { x.QuantityTypeId, x.MemberNo });
                    table.ForeignKey(
                        name: "FK_QuantityTypeMembers_QuantityTypes_QuantityTypeId",
                        column: x => x.QuantityTypeId,
                        principalTable: "QuantityTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RequestItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemNo = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    PartName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    QuantityTypeId = table.Column<long>(type: "bigint", nullable: true),
                    BaseUnitsPerUnit = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    MaterialId = table.Column<long>(type: "bigint", nullable: true),
                    WeightKgPerBaseUnit = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LengthMm = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DiameterMm = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestItems_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestItems_QuantityTypes_QuantityTypeId",
                        column: x => x.QuantityTypeId,
                        principalTable: "QuantityTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestItems_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestItems_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Action = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ActionByDepartmentCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ContractId = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    ContractNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContractDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Decision = table.Column<int>(type: "int", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContractId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                name: "ItemImportRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SheetRowNo = table.Column<int>(type: "int", nullable: false),
                    ItemNo = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ItemImportId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemImportRows_ItemImports_ItemImportId",
                        column: x => x.ItemImportId,
                        principalTable: "ItemImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialInquiryHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Action = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: true),
                    Answer = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ActionByDepartmentCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InquiryId = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    MaterialGrade = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MaterialForm = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DiameterMm = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LengthMm = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    OtherDimension = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequiredQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    QuantityUnitId = table.Column<long>(type: "bigint", nullable: false),
                    StandardOrSpec = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InquiryId = table.Column<long>(type: "bigint", nullable: false)
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
                name: "CustomerDispatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    DispatchDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DocumentReference = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecordedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDispatches_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDispatches_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemContractDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    Requirement = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DecidedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemContractDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemContractDecisions_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemContractDecisions_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemContractDecisions_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemCostCalculations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    Formula = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AcceptedBaseUnits = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedBaseUnits = table.Column<long>(type: "bigint", nullable: false),
                    MaterialId = table.Column<long>(type: "bigint", nullable: true),
                    MaterialPriceId = table.Column<long>(type: "bigint", nullable: true),
                    PriceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MachiningTotalRial = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaterialTotalRial = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalRial = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitCostRial = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CalculatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCostCalculations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemCostCalculations_MaterialPrices_MaterialPriceId",
                        column: x => x.MaterialPriceId,
                        principalTable: "MaterialPrices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostCalculations_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostCalculations_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostCalculations_Users_CalculatedByUserId",
                        column: x => x.CalculatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemStages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StageNo = table.Column<int>(type: "int", nullable: false),
                    StageTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlannedMinutesPerPiece = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: true),
                    MachineId = table.Column<long>(type: "bigint", nullable: true),
                    PlanningPosition = table.Column<int>(type: "int", nullable: true),
                    AvailableSinceUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StoppedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StopReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StoppedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    InputBaseUnits = table.Column<long>(type: "bigint", nullable: true),
                    AwaitingQcBaseUnits = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    AcceptedBaseUnits = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    ReworkBaseUnits = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    ScrapBaseUnits = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemStages", x => x.Id);
                    table.UniqueConstraint("AK_ItemStages_RequestItemId_StageNo", x => new { x.RequestItemId, x.StageNo });
                    table.ForeignKey(
                        name: "FK_ItemStages_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemStages_ProductionUnits_ProductionUnitId",
                        column: x => x.ProductionUnitId,
                        principalTable: "ProductionUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemStages_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemStages_Users_StoppedByUserId",
                        column: x => x.StoppedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseReceipts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedBaseUnits = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceivedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseReceipts_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseReceipts_Users_ReceivedByUserId",
                        column: x => x.ReceivedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractDocumentLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LinkedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContractVersionId = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "RawMaterialInquiryResponses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Answer = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RespondedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RespondedByDepartmentCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LineId = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "ItemPriceDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CalculationId = table.Column<long>(type: "bigint", nullable: false),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    ProposedPriceRial = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedPriceRial = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContractReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPriceDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemPriceDecisions_ItemCostCalculations_CalculationId",
                        column: x => x.CalculationId,
                        principalTable: "ItemCostCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemPriceDecisions_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemPriceDecisions_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemPriceDecisions_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyJobReportLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    MachineId = table.Column<long>(type: "bigint", nullable: false),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    StageNo = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DowntimeMinutes = table.Column<int>(type: "int", nullable: false),
                    DowntimeReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessedBaseUnits = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Flags = table.Column<int>(type: "int", nullable: false),
                    DailyJobReportId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyJobReportLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyJobReportLines_DailyJobReports_DailyJobReportId",
                        column: x => x.DailyJobReportId,
                        principalTable: "DailyJobReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReportLines_ItemStages_RequestItemId_StageNo",
                        columns: x => new { x.RequestItemId, x.StageNo },
                        principalTable: "ItemStages",
                        principalColumns: new[] { "RequestItemId", "StageNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReportLines_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJobReportLines_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QcApprovals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StageNo = table.Column<int>(type: "int", nullable: false),
                    ApprovedQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ApprovedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcApprovals_ItemStages_RequestItemId_StageNo",
                        columns: x => new { x.RequestItemId, x.StageNo },
                        principalTable: "ItemStages",
                        principalColumns: new[] { "RequestItemId", "StageNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcApprovals_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcApprovals_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QcInspectionLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    StageNo = table.Column<int>(type: "int", nullable: false),
                    InspectionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Disposition = table.Column<int>(type: "int", nullable: false),
                    BaseUnits = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
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
                name: "QueueOverrideHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    StageNo = table.Column<int>(type: "int", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldRank = table.Column<int>(type: "int", nullable: true),
                    NewRank = table.Column<int>(type: "int", nullable: true),
                    OldOrder = table.Column<int>(type: "int", nullable: true),
                    NewOrder = table.Column<int>(type: "int", nullable: true)
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    StageNo = table.Column<int>(type: "int", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SetByUserId = table.Column<long>(type: "bigint", nullable: false),
                    SetAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NeedsReview = table.Column<bool>(type: "bit", nullable: false),
                    ReviewNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewFlaggedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                name: "ShortfallDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    StageNo = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ShortfallBaseUnits = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DecidedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "WorkSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    StageNo = table.Column<int>(type: "int", nullable: false),
                    ProductionUnitId = table.Column<long>(type: "bigint", nullable: false),
                    MachineId = table.Column<long>(type: "bigint", nullable: false),
                    OperatorId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PredecessorSessionId = table.Column<long>(type: "bigint", nullable: true),
                    QualificationWarning = table.Column<bool>(type: "bit", nullable: false),
                    OutsideShiftReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DispatchedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    DispatchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishKind = table.Column<int>(type: "int", nullable: true),
                    FinishNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TimeApprovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    TimeApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                name: "RawMaterialInquiryDispositions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResponseId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewRoundInquiryId = table.Column<long>(type: "bigint", nullable: true),
                    DecidedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    DecidedByDepartmentCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LineId = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResponseId = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExternalPurchase = table.Column<bool>(type: "bit", nullable: false),
                    DecidedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    DecidedByDepartmentCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LineId = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "ItemFinishedSent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestItemId = table.Column<long>(type: "bigint", nullable: false),
                    ReceiptId = table.Column<long>(type: "bigint", nullable: false),
                    PriceDecisionId = table.Column<long>(type: "bigint", nullable: false),
                    ApprovedPriceRial = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MarkedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    MarkedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemFinishedSent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemFinishedSent_ItemPriceDecisions_PriceDecisionId",
                        column: x => x.PriceDecisionId,
                        principalTable: "ItemPriceDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemFinishedSent_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemFinishedSent_Users_MarkedByUserId",
                        column: x => x.MarkedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemFinishedSent_WarehouseReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "WarehouseReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemCostLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    JobReportLineId = table.Column<long>(type: "bigint", nullable: true),
                    SessionIds = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MachineId = table.Column<long>(type: "bigint", nullable: true),
                    RateId = table.Column<long>(type: "bigint", nullable: true),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Minutes = table.Column<int>(type: "int", nullable: true),
                    WeightKg = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    UnitPriceRial = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AmountRial = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CalculationId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCostLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemCostLines_DailyJobReportLines_JobReportLineId",
                        column: x => x.JobReportLineId,
                        principalTable: "DailyJobReportLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostLines_ItemCostCalculations_CalculationId",
                        column: x => x.CalculationId,
                        principalTable: "ItemCostCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCostLines_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkSessionCorrections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Boundary = table.Column<int>(type: "int", nullable: false),
                    PauseIndex = table.Column<int>(type: "int", nullable: false),
                    OldValueUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewValueUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CorrectedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CorrectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SessionId = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ByUserId = table.Column<long>(type: "bigint", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SessionId = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<long>(type: "bigint", nullable: false),
                    OperatorId = table.Column<long>(type: "bigint", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    EndedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    EndedBy = table.Column<int>(type: "int", nullable: true),
                    SessionId = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReasonId = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    OutageEventId = table.Column<long>(type: "bigint", nullable: true),
                    PausedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    PausedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResumedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    SessionId = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.InsertData(
                table: "UnitsOfMeasure",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "CreatedByUserId", "IsActive", "Kind", "Name", "UpdatedAtUtc", "Version" },
                values: new object[,]
                {
                    { 1L, "PCS", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, 1, "عدد (قطعه)", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 2L, "PAIR", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, 1, "جفت", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 3L, "KG", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, 2, "کیلوگرم", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 4L, "MM", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, 3, "میلی‌متر", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L }
                });

            migrationBuilder.InsertData(
                table: "WeeklyWorkdays",
                columns: new[] { "Day", "IsWorkday", "UpdatedAtUtc", "Version" },
                values: new object[,]
                {
                    { 0, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 1, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 2, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 3, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 4, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 5, false, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 6, true, new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1L }
                });

            migrationBuilder.InsertData(
                table: "QuantityTypes",
                columns: new[] { "Id", "BaseUnitId", "BaseUnitsPerType", "Code", "CreatedAtUtc", "CreatedByUserId", "IsActive", "Name", "UpdatedAtUtc", "Version" },
                values: new object[,]
                {
                    { 1L, 1L, 1, "PIECE", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "قطعه", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L },
                    { 2L, 1L, 2, "PAIR", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "جفت", new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L }
                });

            migrationBuilder.InsertData(
                table: "UnitConversions",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Factor", "FromUnitId", "IsActive", "ToUnitId", "UpdatedAtUtc", "Version" },
                values: new object[] { 1L, new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, 2m, 2L, true, 1L, new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Utc), 1L });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_UserId",
                table: "AuditLog",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarExceptions_Date",
                table: "CalendarExceptions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarExceptions_ProductionUnitId",
                table: "CalendarExceptions",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarExceptions_RecordedByUserId",
                table: "CalendarExceptions",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarExceptions_ShiftTemplateId",
                table: "CalendarExceptions",
                column: "ShiftTemplateId");

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

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDispatches_RecordedByUserId",
                table: "CustomerDispatches",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDispatches_RequestItemId",
                table: "CustomerDispatches",
                column: "RequestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedByUserId",
                table: "Customers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportEvents_DailyJobReportId",
                table: "DailyJobReportEvents",
                column: "DailyJobReportId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportEvents_UserId",
                table: "DailyJobReportEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportLines_DailyJobReportId",
                table: "DailyJobReportLines",
                column: "DailyJobReportId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportLines_MachineId_StartUtc",
                table: "DailyJobReportLines",
                columns: new[] { "MachineId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportLines_RequestId",
                table: "DailyJobReportLines",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReportLines_RequestItemId_StageNo",
                table: "DailyJobReportLines",
                columns: new[] { "RequestItemId", "StageNo" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_CreatedByUserId",
                table: "DailyJobReports",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_OperatorId_WorkDate_ShiftTemplateId",
                table: "DailyJobReports",
                columns: new[] { "OperatorId", "WorkDate", "ShiftTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_ProductionUnitId",
                table: "DailyJobReports",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_ReviewedByUserId",
                table: "DailyJobReports",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_ShiftTemplateId",
                table: "DailyJobReports",
                column: "ShiftTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJobReports_WorkDate_ProductionUnitId",
                table: "DailyJobReports",
                columns: new[] { "WorkDate", "ProductionUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Code",
                table: "Departments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UploadedByUserId",
                table: "Documents",
                column: "UploadedByUserId");

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
                name: "IX_ItemContractDecisions_DecidedByUserId",
                table: "ItemContractDecisions",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemContractDecisions_RequestId",
                table: "ItemContractDecisions",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemContractDecisions_RequestItemId",
                table: "ItemContractDecisions",
                column: "RequestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostCalculations_CalculatedByUserId",
                table: "ItemCostCalculations",
                column: "CalculatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostCalculations_MaterialId",
                table: "ItemCostCalculations",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostCalculations_MaterialPriceId",
                table: "ItemCostCalculations",
                column: "MaterialPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostCalculations_RequestItemId_SequenceNo",
                table: "ItemCostCalculations",
                columns: new[] { "RequestItemId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostLines_CalculationId",
                table: "ItemCostLines",
                column: "CalculationId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostLines_JobReportLineId",
                table: "ItemCostLines",
                column: "JobReportLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCostLines_MachineId",
                table: "ItemCostLines",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemFinishedSent_MarkedByUserId",
                table: "ItemFinishedSent",
                column: "MarkedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemFinishedSent_PriceDecisionId",
                table: "ItemFinishedSent",
                column: "PriceDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemFinishedSent_ReceiptId",
                table: "ItemFinishedSent",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemFinishedSent_RequestItemId",
                table: "ItemFinishedSent",
                column: "RequestItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemImportRows_ItemImportId",
                table: "ItemImportRows",
                column: "ItemImportId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemImports_ImportedByUserId",
                table: "ItemImports",
                column: "ImportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemImports_RequestId",
                table: "ItemImports",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemPriceDecisions_ApprovedByUserId",
                table: "ItemPriceDecisions",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemPriceDecisions_CalculationId",
                table: "ItemPriceDecisions",
                column: "CalculationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemPriceDecisions_RequestItemId",
                table: "ItemPriceDecisions",
                column: "RequestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemPriceDecisions_SubmittedByUserId",
                table: "ItemPriceDecisions",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemStages_MachineId",
                table: "ItemStages",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemStages_ProductionUnitId",
                table: "ItemStages",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemStages_StoppedByUserId",
                table: "ItemStages",
                column: "StoppedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineHourlyRates_ChangedByUserId",
                table: "MachineHourlyRates",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineHourlyRates_MachineId_EffectiveFrom",
                table: "MachineHourlyRates",
                columns: new[] { "MachineId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Machines_Code",
                table: "Machines",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Machines_CreatedByUserId",
                table: "Machines",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_ProductionUnitId",
                table: "Machines",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialPrices_ChangedByUserId",
                table: "MaterialPrices",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialPrices_MaterialId_EffectiveFrom",
                table: "MaterialPrices",
                columns: new[] { "MaterialId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Code",
                table: "Materials",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_CreatedByUserId",
                table: "Materials",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorMachineAssignments_AssignedByUserId",
                table: "OperatorMachineAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorMachineAssignments_ClosedByUserId",
                table: "OperatorMachineAssignments",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorMachineAssignments_MachineId",
                table: "OperatorMachineAssignments",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorMachineAssignments_OperatorId_MachineId",
                table: "OperatorMachineAssignments",
                columns: new[] { "OperatorId", "MachineId" });

            migrationBuilder.CreateIndex(
                name: "IX_OperatorMachineQualifications_MachineId",
                table: "OperatorMachineQualifications",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorMachineQualifications_OperatorId_MachineId",
                table: "OperatorMachineQualifications",
                columns: new[] { "OperatorId", "MachineId" });

            migrationBuilder.CreateIndex(
                name: "IX_OperatorMachineQualifications_RecordedByUserId",
                table: "OperatorMachineQualifications",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorOffDays_OperatorId_FromDate",
                table: "OperatorOffDays",
                columns: new[] { "OperatorId", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OperatorOffDays_RecordedByUserId",
                table: "OperatorOffDays",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Operators_Code",
                table: "Operators",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Operators_CreatedByUserId",
                table: "Operators",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Operators_UserId",
                table: "Operators",
                column: "UserId");

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
                name: "IX_ProductionUnits_Code",
                table: "ProductionUnits",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionUnits_CreatedByUserId",
                table: "ProductionUnits",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QcApprovals_ApprovedByUserId",
                table: "QcApprovals",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QcApprovals_RequestItemId_StageNo",
                table: "QcApprovals",
                columns: new[] { "RequestItemId", "StageNo" });

            migrationBuilder.CreateIndex(
                name: "IX_QcInspectionLines_DecidedByUserId",
                table: "QcInspectionLines",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QcInspectionLines_RequestItemId_StageNo",
                table: "QcInspectionLines",
                columns: new[] { "RequestItemId", "StageNo" });

            migrationBuilder.CreateIndex(
                name: "IX_QuantityTypes_BaseUnitId",
                table: "QuantityTypes",
                column: "BaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_QuantityTypes_Code",
                table: "QuantityTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuantityTypes_CreatedByUserId",
                table: "QuantityTypes",
                column: "CreatedByUserId");

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
                name: "IX_RawMaterialInquiries_CreatedByUserId",
                table: "RawMaterialInquiries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialInquiries_RequestId_RoundNo",
                table: "RawMaterialInquiries",
                columns: new[] { "RequestId", "RoundNo" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_RequestDocumentLinks_DocumentId",
                table: "RequestDocumentLinks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestDocumentLinks_LinkedByUserId",
                table: "RequestDocumentLinks",
                column: "LinkedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestDocumentLinks_RequestId_Type",
                table: "RequestDocumentLinks",
                columns: new[] { "RequestId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_CreatedByUserId",
                table: "RequestItems",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_MaterialId",
                table: "RequestItems",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_QuantityTypeId",
                table: "RequestItems",
                column: "QuantityTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_RequestId_ItemNo",
                table: "RequestItems",
                columns: new[] { "RequestId", "ItemNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CreatedByUserId",
                table: "Requests",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CustomerId_RequestNo_JalaliRequestYear",
                table: "Requests",
                columns: new[] { "CustomerId", "RequestNo", "JalaliRequestYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestWorkflowEntries_ActionByUserId",
                table: "RequestWorkflowEntries",
                column: "ActionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestWorkflowEntries_RequestId",
                table: "RequestWorkflowEntries",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Code",
                table: "Roles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTemplates_Code",
                table: "ShiftTemplates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTemplates_CreatedByUserId",
                table: "ShiftTemplates",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortfallDecisions_DecidedByUserId",
                table: "ShortfallDecisions",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortfallDecisions_RequestItemId_StageNo",
                table: "ShortfallDecisions",
                columns: new[] { "RequestItemId", "StageNo" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemSetup_RecoveryTargetUserId",
                table: "SystemSetup",
                column: "RecoveryTargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitConversions_CreatedByUserId",
                table: "UnitConversions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitConversions_FromUnitId_ToUnitId",
                table: "UnitConversions",
                columns: new[] { "FromUnitId", "ToUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitConversions_ToUnitId",
                table: "UnitConversions",
                column: "ToUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitShifts_CreatedByUserId",
                table: "UnitShifts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitShifts_ProductionUnitId_ShiftTemplateId",
                table: "UnitShifts",
                columns: new[] { "ProductionUnitId", "ShiftTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitShifts_ShiftTemplateId",
                table: "UnitShifts",
                column: "ShiftTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitsOfMeasure_Code",
                table: "UnitsOfMeasure",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitsOfMeasure_CreatedByUserId",
                table: "UnitsOfMeasure",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DepartmentId",
                table: "Users",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UsernameKey",
                table: "Users",
                column: "UsernameKey",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseReceipts_ReceivedByUserId",
                table: "WarehouseReceipts",
                column: "ReceivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseReceipts_RequestItemId",
                table: "WarehouseReceipts",
                column: "RequestItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_CancelledByUserId",
                table: "WorkerSchedules",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_CreatedByUserId",
                table: "WorkerSchedules",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_Date_ProductionUnitId",
                table: "WorkerSchedules",
                columns: new[] { "Date", "ProductionUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_MachineId",
                table: "WorkerSchedules",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_OperatorId_Date",
                table: "WorkerSchedules",
                columns: new[] { "OperatorId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_ProductionUnitId",
                table: "WorkerSchedules",
                column: "ProductionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerSchedules_ShiftTemplateId",
                table: "WorkerSchedules",
                column: "ShiftTemplateId");

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
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "CalendarExceptions");

            migrationBuilder.DropTable(
                name: "ContractDocumentLinks");

            migrationBuilder.DropTable(
                name: "ContractHistory");

            migrationBuilder.DropTable(
                name: "CustomerDispatches");

            migrationBuilder.DropTable(
                name: "DailyJobReportEvents");

            migrationBuilder.DropTable(
                name: "DrawingClarifications");

            migrationBuilder.DropTable(
                name: "ItemContractDecisions");

            migrationBuilder.DropTable(
                name: "ItemCostLines");

            migrationBuilder.DropTable(
                name: "ItemFinishedSent");

            migrationBuilder.DropTable(
                name: "ItemImportRows");

            migrationBuilder.DropTable(
                name: "MachineHourlyRates");

            migrationBuilder.DropTable(
                name: "OperatorMachineAssignments");

            migrationBuilder.DropTable(
                name: "OperatorMachineQualifications");

            migrationBuilder.DropTable(
                name: "OperatorOffDays");

            migrationBuilder.DropTable(
                name: "OvertimeAssignments");

            migrationBuilder.DropTable(
                name: "OvertimeLimits");

            migrationBuilder.DropTable(
                name: "QcApprovals");

            migrationBuilder.DropTable(
                name: "QcInspectionLines");

            migrationBuilder.DropTable(
                name: "QuantityTypeMembers");

            migrationBuilder.DropTable(
                name: "QueueOverrideHistory");

            migrationBuilder.DropTable(
                name: "QueueOverrides");

            migrationBuilder.DropTable(
                name: "RawMaterialInquiryDispositions");

            migrationBuilder.DropTable(
                name: "RawMaterialInquiryHistory");

            migrationBuilder.DropTable(
                name: "RawMaterialInquirySupplyRoutes");

            migrationBuilder.DropTable(
                name: "RequestDocumentLinks");

            migrationBuilder.DropTable(
                name: "RequestWorkflowEntries");

            migrationBuilder.DropTable(
                name: "ShortfallDecisions");

            migrationBuilder.DropTable(
                name: "SystemSetup");

            migrationBuilder.DropTable(
                name: "UnitConversions");

            migrationBuilder.DropTable(
                name: "UnitShifts");

            migrationBuilder.DropTable(
                name: "UserCredentials");

            migrationBuilder.DropTable(
                name: "UserUnitDesignations");

            migrationBuilder.DropTable(
                name: "WeeklyWorkdays");

            migrationBuilder.DropTable(
                name: "WorkerSchedules");

            migrationBuilder.DropTable(
                name: "WorkSessionCorrections");

            migrationBuilder.DropTable(
                name: "WorkSessionEvents");

            migrationBuilder.DropTable(
                name: "WorkSessionIntervals");

            migrationBuilder.DropTable(
                name: "WorkSessionPauses");

            migrationBuilder.DropTable(
                name: "ContractVersions");

            migrationBuilder.DropTable(
                name: "DailyJobReportLines");

            migrationBuilder.DropTable(
                name: "ItemPriceDecisions");

            migrationBuilder.DropTable(
                name: "WarehouseReceipts");

            migrationBuilder.DropTable(
                name: "ItemImports");

            migrationBuilder.DropTable(
                name: "RawMaterialInquiryResponses");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "OutageEvents");

            migrationBuilder.DropTable(
                name: "WorkSessions");

            migrationBuilder.DropTable(
                name: "Contracts");

            migrationBuilder.DropTable(
                name: "DailyJobReports");

            migrationBuilder.DropTable(
                name: "ItemCostCalculations");

            migrationBuilder.DropTable(
                name: "RawMaterialInquiryLines");

            migrationBuilder.DropTable(
                name: "PauseReasons");

            migrationBuilder.DropTable(
                name: "ItemStages");

            migrationBuilder.DropTable(
                name: "Operators");

            migrationBuilder.DropTable(
                name: "ShiftTemplates");

            migrationBuilder.DropTable(
                name: "MaterialPrices");

            migrationBuilder.DropTable(
                name: "RawMaterialInquiries");

            migrationBuilder.DropTable(
                name: "Machines");

            migrationBuilder.DropTable(
                name: "RequestItems");

            migrationBuilder.DropTable(
                name: "ProductionUnits");

            migrationBuilder.DropTable(
                name: "Materials");

            migrationBuilder.DropTable(
                name: "QuantityTypes");

            migrationBuilder.DropTable(
                name: "Requests");

            migrationBuilder.DropTable(
                name: "UnitsOfMeasure");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
