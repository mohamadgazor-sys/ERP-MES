using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ExternalStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ProductionUnits",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "INTERNAL_PRODUCTION");

            migrationBuilder.AddColumn<int>(
                name: "ExpectedLeadTimeDays",
                table: "ItemStages",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "ProductionUnits");

            migrationBuilder.DropColumn(
                name: "ExpectedLeadTimeDays",
                table: "ItemStages");
        }
    }
}
