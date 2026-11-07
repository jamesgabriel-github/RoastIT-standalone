using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoastIT.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_items_order_key",
                table: "items");

            migrationBuilder.DropIndex(
                name: "IX_items_product_id",
                table: "items");

            migrationBuilder.CreateIndex(
                name: "ix_products_active",
                table: "products",
                column: "active");

            migrationBuilder.CreateIndex(
                name: "ix_orders_pickup_at",
                table: "orders",
                column: "pickup_at");

            migrationBuilder.CreateIndex(
                name: "ix_items_order_status_id",
                table: "items",
                columns: new[] { "order_key", "status", "booking_id" });

            migrationBuilder.CreateIndex(
                name: "ix_items_product_status",
                table: "items",
                columns: new[] { "product_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_items_status",
                table: "items",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_active",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_orders_pickup_at",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_items_order_status_id",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_product_status",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_status",
                table: "items");

            migrationBuilder.CreateIndex(
                name: "IX_items_order_key",
                table: "items",
                column: "order_key");

            migrationBuilder.CreateIndex(
                name: "IX_items_product_id",
                table: "items",
                column: "product_id");
        }
    }
}
