using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrenotaConMe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "cover_version",
                table: "tenants",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "logo_version",
                table: "tenants",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenant_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_images_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_images_tenant_id_kind",
                table: "tenant_images",
                columns: new[] { "tenant_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_images");

            migrationBuilder.DropColumn(
                name: "cover_version",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "logo_version",
                table: "tenants");
        }
    }
}
