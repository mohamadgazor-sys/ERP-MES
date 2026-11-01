using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class UnitShiftRegularDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RegularDays",
                table: "UnitShifts",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegularDays",
                table: "UnitShifts");
        }
    }
}
