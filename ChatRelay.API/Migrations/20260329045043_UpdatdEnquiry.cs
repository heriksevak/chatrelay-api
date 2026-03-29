using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatRelay.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdatdEnquiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "Enquiries",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "Enquiries");
        }
    }
}
