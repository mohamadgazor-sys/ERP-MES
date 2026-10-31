using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ItemNumberingAndAssemblies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NextRootSequence",
                table: "Requests",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "CustomerItemRef",
                table: "RequestItems",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<bool>(
                name: "IsAssembly",
                table: "RequestItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ItemSequence",
                table: "RequestItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NextChildSequence",
                table: "RequestItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "ParentItemId",
                table: "RequestItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_ParentItemId",
                table: "RequestItems",
                column: "ParentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_RequestId_CustomerItemRef",
                table: "RequestItems",
                columns: new[] { "RequestId", "CustomerItemRef" },
                unique: true,
                filter: "[CustomerItemRef] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestItems_RequestItems_ParentItemId",
                table: "RequestItems",
                column: "ParentItemId",
                principalTable: "RequestItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestItems_RequestItems_ParentItemId",
                table: "RequestItems");

            migrationBuilder.DropIndex(
                name: "IX_RequestItems_ParentItemId",
                table: "RequestItems");

            migrationBuilder.DropIndex(
                name: "IX_RequestItems_RequestId_CustomerItemRef",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "NextRootSequence",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "CustomerItemRef",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "IsAssembly",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "ItemSequence",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "NextChildSequence",
                table: "RequestItems");

            migrationBuilder.DropColumn(
                name: "ParentItemId",
                table: "RequestItems");
        }
    }
}
