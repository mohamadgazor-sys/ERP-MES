using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RepairFieldsOnItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DeliveryDate",
                table: "RequestItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseItemCode",
                table: "RequestItems",
                type: "TEXT",
                maxLength: 40,
                nullable: true,
                collation: "BINARY");

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_WarehouseItemCode",
                table: "RequestItems",
                column: "WarehouseItemCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestItems_WarehouseItemCode",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "DeliveryDate",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "WarehouseItemCode",
                table: "RequestItems");
        }
    }
}
