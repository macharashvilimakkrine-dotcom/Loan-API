using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoanApi.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public partial class SeparateAccountants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accountants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FirstName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accountants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accountants_Username",
                table: "Accountants",
                column: "Username",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO Accountants (FirstName, LastName, Username, PasswordHash)
                SELECT FirstName, LastName, Username, PasswordHash
                FROM Users
                WHERE Role = N'Accountant';

                DELETE FROM Users
                WHERE Role = N'Accountant';
                """);

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.Sql(
                """
                INSERT INTO Users
                    (FirstName, LastName, Username, Age, Email, MonthlyIncome, IsBlocked, BlockedUntil, PasswordHash, Role)
                SELECT
                    FirstName, LastName, Username, 0, CONCAT(Username, N'@loanapi.local'), 0, 0, NULL, PasswordHash, N'Accountant'
                FROM Accountants;
                """);

            migrationBuilder.DropTable(
                name: "Accountants");
        }
    }
}
