using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoastIT.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingsDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_counters",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    last_seq = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_counters", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    key = table.Column<Guid>(type: "TEXT", nullable: false),
                    customer = table.Column<string>(type: "TEXT", nullable: true),
                    phone = table.Column<string>(type: "TEXT", nullable: true),
                    order_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    pickup_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    total_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    discount_amount = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    amount_paid = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.key);
                    table.CheckConstraint("ck_orders_discount", "discount_amount >= 0 AND discount_amount <= total_amount");
                    table.CheckConstraint("ck_orders_paid", "amount_paid >= 0");
                    table.CheckConstraint("ck_orders_total", "total_amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    booking_id = table.Column<string>(type: "TEXT", nullable: false),
                    order_key = table.Column<Guid>(type: "TEXT", nullable: false),
                    product_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    started_cooking_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.booking_id);
                    table.ForeignKey(
                        name: "FK_items_orders_order_key",
                        column: x => x.order_key,
                        principalTable: "orders",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order_key = table.Column<Guid>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount > 0");
                    table.ForeignKey(
                        name: "FK_payments_orders_order_key",
                        column: x => x.order_key,
                        principalTable: "orders",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_items_order_key",
                table: "items",
                column: "order_key");

            migrationBuilder.CreateIndex(
                name: "IX_items_product_id",
                table: "items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_payments_order_key",
                table: "payments",
                column: "order_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_counters");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "orders");
        }
    }
}
