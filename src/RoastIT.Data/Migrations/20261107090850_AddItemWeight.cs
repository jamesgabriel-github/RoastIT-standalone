using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoastIT.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "weight_g",
                table: "items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1000);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "weight_g",
                table: "items");
        }
    }
}
