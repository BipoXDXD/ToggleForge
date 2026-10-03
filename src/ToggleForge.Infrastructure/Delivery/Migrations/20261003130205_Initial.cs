using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToggleForge.Infrastructure.Delivery.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "delivery");

            migrationBuilder.CreateTable(
                name: "credentials",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    prefix = table.Column<string>(type: "text", nullable: false),
                    secret_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credentials", x => x.id);
                    table.CheckConstraint("ck_credentials_name", "char_length(name) between 1 and 100");
                    table.CheckConstraint("ck_credentials_prefix", "prefix ~ '^tfsdk_[A-Za-z0-9_-]{4}$'");
                    table.CheckConstraint("ck_credentials_secret_hash", "secret_hash ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_credentials_environment_id",
                schema: "delivery",
                table: "credentials",
                column: "environment_id");

            migrationBuilder.CreateIndex(
                name: "ix_credentials_secret_hash",
                schema: "delivery",
                table: "credentials",
                column: "secret_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credentials",
                schema: "delivery");
        }
    }
}
