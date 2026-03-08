using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatRelay.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderMessageId",
                table: "Messages",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAt",
                table: "Messages",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderMessageId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "Messages");
        }
    }
}
