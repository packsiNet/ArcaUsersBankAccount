using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SafeTunnels.Account.Migrations
{
    /// <inheritdoc />
    public partial class AddIbanHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IbanHash",
                table: "BankAccounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_IbanHash",
                table: "BankAccounts",
                column: "IbanHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankAccounts_IbanHash",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "IbanHash",
                table: "BankAccounts");
        }
    }
}
