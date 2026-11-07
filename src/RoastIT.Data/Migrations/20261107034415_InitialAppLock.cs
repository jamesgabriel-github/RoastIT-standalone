using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoastIT.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAppLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_lock",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    pin_hash = table.Column<string>(type: "TEXT", nullable: false),
                    recovery_code_hash = table.Column<string>(type: "TEXT", nullable: false),
                    failed_attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    locked_until = table.Column<DateTime>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_lock", x => x.id);
                    table.CheckConstraint("ck_app_lock_single_row", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "app_meta",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_meta", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    target_type = table.Column<string>(type: "TEXT", nullable: false),
                    target_id = table.Column<string>(type: "TEXT", nullable: false),
                    details = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "app_meta",
                columns: new[] { "key", "value" },
                values: new object[] { "app_id", "roastline" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_lock");

            migrationBuilder.DropTable(
                name: "app_meta");

            migrationBuilder.DropTable(
                name: "audit_logs");
        }
    }
}
