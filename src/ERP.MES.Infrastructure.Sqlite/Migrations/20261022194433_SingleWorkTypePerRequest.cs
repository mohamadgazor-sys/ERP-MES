using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.MES.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class SingleWorkTypePerRequest : Migration
    {
        /// <summary>
        /// Moves the work type from the items to the request (Decision Log #84: one authoritative type per request; mixed-type requests are prohibited).
        /// Existing requests take the single type of their items. A request whose items have DIFFERENT types, and a request without items (its type cannot be
        /// known), get no type, so the NOT NULL step below fails and the whole migration is rolled back: nothing is chosen silently and no data is changed or lost.
        /// Such a database must be fixed by hand (split the mixed request; give an empty one a type) or, if it is a demonstration database, reset.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkType",
                table: "Requests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE Requests SET WorkType = (" +
                "SELECT CASE WHEN COUNT(DISTINCT i.WorkType) = 1 THEN MIN(i.WorkType) END FROM RequestItems i WHERE i.RequestId = Requests.Id);");

            migrationBuilder.AlterColumn<int>(
                name: "WorkType",
                table: "Requests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "WorkType",
                table: "RequestItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkType",
                table: "RequestItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                "UPDATE RequestItems SET WorkType = (SELECT r.WorkType FROM Requests r WHERE r.Id = RequestItems.RequestId);");

            migrationBuilder.DropColumn(
                name: "WorkType",
                table: "Requests");
        }
    }
}
