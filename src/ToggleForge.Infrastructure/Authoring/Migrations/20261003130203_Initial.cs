using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToggleForge.Infrastructure.Authoring.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "authoring");

            migrationBuilder.CreateTable(
                name: "projects",
                schema: "authoring",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.CheckConstraint("ck_projects_key", "key ~ '^[a-z0-9][a-z0-9._-]{0,63}$'");
                    table.CheckConstraint("ck_projects_name", "char_length(name) between 1 and 100");
                });

            migrationBuilder.CreateTable(
                name: "environments",
                schema: "authoring",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    draft_revision = table.Column<long>(type: "bigint", nullable: false),
                    current_version = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_environments", x => x.id);
                    table.CheckConstraint("ck_environments_current_version", "current_version >= 1");
                    table.CheckConstraint("ck_environments_draft_revision", "draft_revision >= 1");
                    table.CheckConstraint("ck_environments_key", "key ~ '^[a-z0-9][a-z0-9._-]{0,63}$'");
                    table.CheckConstraint("ck_environments_name", "char_length(name) between 1 and 100");
                    table.ForeignKey(
                        name: "fk_environments_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "authoring",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "draft_flags",
                schema: "authoring",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    on_value = table.Column<bool>(type: "boolean", nullable: false),
                    off_value = table.Column<bool>(type: "boolean", nullable: false),
                    rollout = table.Column<int>(type: "integer", nullable: false),
                    seed = table.Column<string>(type: "text", nullable: false),
                    changed_at_revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_draft_flags", x => new { x.environment_id, x.key });
                    table.CheckConstraint("ck_draft_flags_key", "key ~ '^[a-z0-9][a-z0-9._-]{0,63}$'");
                    table.CheckConstraint("ck_draft_flags_rollout", "rollout between 0 and 10000");
                    table.ForeignKey(
                        name: "fk_draft_flags_environments_environment_id",
                        column: x => x.environment_id,
                        principalSchema: "authoring",
                        principalTable: "environments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "publications",
                schema: "authoring",
                columns: table => new
                {
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    restored_version = table.Column<long>(type: "bigint", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publications", x => new { x.environment_id, x.version });
                    table.CheckConstraint("ck_publications_origin", "origin in ('publication', 'rollback')");
                    table.CheckConstraint("ck_publications_restored_version", "(origin = 'rollback') = (restored_version is not null)");
                    table.CheckConstraint("ck_publications_summary", "char_length(summary) between 1 and 280");
                    table.CheckConstraint("ck_publications_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_publications_environments_environment_id",
                        column: x => x.environment_id,
                        principalSchema: "authoring",
                        principalTable: "environments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "authoring",
                columns: table => new
                {
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => new { x.member_id, x.key });
                    table.CheckConstraint("ck_idempotency_records_key", "key ~ '^[A-Za-z0-9_-]{16,64}$'");
                    table.ForeignKey(
                        name: "fk_idempotency_records_publications_environment_id_version",
                        columns: x => new { x.environment_id, x.version },
                        principalSchema: "authoring",
                        principalTable: "publications",
                        principalColumns: new[] { "environment_id", "version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publication_flags",
                schema: "authoring",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    on_value = table.Column<bool>(type: "boolean", nullable: false),
                    off_value = table.Column<bool>(type: "boolean", nullable: false),
                    rollout_basis_points = table.Column<int>(type: "integer", nullable: false),
                    seed = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publication_flags", x => new { x.environment_id, x.version, x.key });
                    table.CheckConstraint("ck_publication_flags_key", "key ~ '^[a-z0-9][a-z0-9._-]{0,63}$'");
                    table.CheckConstraint("ck_publication_flags_rollout", "rollout_basis_points between 0 and 10000");
                    table.ForeignKey(
                        name: "fk_publication_flags_publications_environment_id_version",
                        columns: x => new { x.environment_id, x.version },
                        principalSchema: "authoring",
                        principalTable: "publications",
                        principalColumns: new[] { "environment_id", "version" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_environments_project_id_key",
                schema: "authoring",
                table: "environments",
                columns: new[] { "project_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_environment_id_version",
                schema: "authoring",
                table: "idempotency_records",
                columns: new[] { "environment_id", "version" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_organization_id_key",
                schema: "authoring",
                table: "projects",
                columns: new[] { "organization_id", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "draft_flags",
                schema: "authoring");

            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "authoring");

            migrationBuilder.DropTable(
                name: "publication_flags",
                schema: "authoring");

            migrationBuilder.DropTable(
                name: "publications",
                schema: "authoring");

            migrationBuilder.DropTable(
                name: "environments",
                schema: "authoring");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "authoring");
        }
    }
}
