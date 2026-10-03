using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToggleForge.Infrastructure.Delivery.Migrations
{
    /// <inheritdoc />
    public partial class CredentialLastUsedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_used_at",
                schema: "delivery",
                table: "credentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_credentials_last_used_after_creation",
                schema: "delivery",
                table: "credentials",
                sql: "last_used_at >= created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_credentials_last_used_after_creation",
                schema: "delivery",
                table: "credentials");

            migrationBuilder.DropColumn(
                name: "last_used_at",
                schema: "delivery",
                table: "credentials");
        }
    }
}
