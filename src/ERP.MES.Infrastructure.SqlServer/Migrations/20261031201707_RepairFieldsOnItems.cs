using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.SqlServer.Migrations
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
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseItemCode",
                table: "RequestItems",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

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
