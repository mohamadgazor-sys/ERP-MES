using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddItemSpecifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiameterMm",
                table: "RequestItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LengthMm",
                table: "RequestItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaterialId",
                table: "RequestItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QuantityTypeId",
                table: "RequestItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKgPerBaseUnit",
                table: "RequestItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MachineId",
                table: "ItemStages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProductionUnitId",
                table: "ItemStages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_MaterialId",
                table: "RequestItems",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_QuantityTypeId",
                table: "RequestItems",
                column: "QuantityTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemStages_MachineId",
                table: "ItemStages",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemStages_ProductionUnitId",
                table: "ItemStages",
                column: "ProductionUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemStages_Machines_MachineId",
                table: "ItemStages",
                column: "MachineId",
                principalTable: "Machines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ItemStages_ProductionUnits_ProductionUnitId",
                table: "ItemStages",
                column: "ProductionUnitId",
                principalTable: "ProductionUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RequestItems_Materials_MaterialId",
                table: "RequestItems",
                column: "MaterialId",
                principalTable: "Materials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RequestItems_QuantityTypes_QuantityTypeId",
                table: "RequestItems",
                column: "QuantityTypeId",
                principalTable: "QuantityTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemStages_Machines_MachineId",
                table: "ItemStages");

            migrationBuilder.DropForeignKey(
                name: "FK_ItemStages_ProductionUnits_ProductionUnitId",
                table: "ItemStages");

            migrationBuilder.DropForeignKey(
                name: "FK_RequestItems_Materials_MaterialId",
                table: "RequestItems");

            migrationBuilder.DropForeignKey(
                name: "FK_RequestItems_QuantityTypes_QuantityTypeId",
                table: "RequestItems");

            migrationBuilder.DropIndex(
                name: "IX_RequestItems_MaterialId",
                table: "RequestItems");

            migrationBuilder.DropIndex(
                name: "IX_RequestItems_QuantityTypeId",
                table: "RequestItems");

            migrationBuilder.DropIndex(
                name: "IX_ItemStages_MachineId",
                table: "ItemStages");

            migrationBuilder.DropIndex(
                name: "IX_ItemStages_ProductionUnitId",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "DiameterMm",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "LengthMm",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "MaterialId",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "QuantityTypeId",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "WeightKgPerBaseUnit",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "MachineId",
                table: "ItemStages");

            migrationBuilder.DropColumn(
                name: "ProductionUnitId",
                table: "ItemStages");
        }
    }
}
