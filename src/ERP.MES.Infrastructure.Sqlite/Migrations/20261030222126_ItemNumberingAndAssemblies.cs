using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
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
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "CustomerItemRef",
                table: "RequestItems",
                type: "TEXT",
                maxLength: 40,
                nullable: true,
                collation: "BINARY");

            migrationBuilder.AddColumn<bool>(
                name: "IsAssembly",
                table: "RequestItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ItemSequence",
                table: "RequestItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NextChildSequence",
                table: "RequestItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "ParentItemId",
                table: "RequestItems",
                type: "INTEGER",
                nullable: true);

            // Existing rows (an older database): the customer's number the item had becomes its customer reference, the item gets the next sequence of its request in the order it was created,
            // and the system number is derived from the request number (Decision Log #121 Q8). The SQL is SQLite's; the SQL Server provider has no older data to carry over.
            migrationBuilder.Sql("UPDATE RequestItems SET CustomerItemRef = ItemNo;");
            migrationBuilder.Sql("UPDATE RequestItems SET ItemSequence = (SELECT COUNT(*) FROM RequestItems b WHERE b.RequestId = RequestItems.RequestId AND b.Id <= RequestItems.Id);");
            migrationBuilder.Sql("UPDATE RequestItems SET ItemNo = (SELECT r.RequestNo FROM Requests r WHERE r.Id = RequestItems.RequestId) || '-' || printf('%03d', ItemSequence);");
            migrationBuilder.Sql("UPDATE Requests SET NextRootSequence = (SELECT COUNT(*) FROM RequestItems i WHERE i.RequestId = Requests.Id) + 1;");
            migrationBuilder.Sql("UPDATE RequestItems SET NextChildSequence = 1;");

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
