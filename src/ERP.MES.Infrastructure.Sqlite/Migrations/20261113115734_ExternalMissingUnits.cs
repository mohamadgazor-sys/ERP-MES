using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ExternalMissingUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LostAtContractorBaseUnits",
                table: "ShortfallDecisions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ExternalMissingBaseUnits",
                table: "ItemStages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LostAtContractorBaseUnits",
                table: "ShortfallDecisions");

            migrationBuilder.DropColumn(
                name: "ExternalMissingBaseUnits",
                table: "ItemStages");
        }
    }
}
