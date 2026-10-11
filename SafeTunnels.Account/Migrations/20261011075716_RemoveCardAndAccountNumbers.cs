using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SafeTunnels.Account.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCardAndAccountNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "CardLastFour",
                table: "BankAccounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "BankAccounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CardLastFour",
                table: "BankAccounts",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
