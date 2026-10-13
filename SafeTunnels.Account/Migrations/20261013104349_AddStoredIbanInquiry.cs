using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SafeTunnels.Account.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredIbanInquiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InquiryCompletedAt",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InquiryResult",
                table: "BankAccounts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InquiryCompletedAt",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "InquiryResult",
                table: "BankAccounts");
        }
    }
}
