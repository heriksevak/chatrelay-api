using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatRelay.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedEnquiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Enquiries",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Enquiries",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "Enquiries");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Enquiries");
        }
    }
}
