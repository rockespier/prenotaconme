using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrenotaConMe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing businesses get the same reminders as new ones (24 h and 2 h before).
            migrationBuilder.AddColumn<int>(
                name: "first_reminder_hours",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "second_reminder_hours",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.CreateTable(
                name: "reminder_preferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder_preferences", x => x.id);
                    table.ForeignKey(
                        name: "fk_reminder_preferences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reminder_preferences_tenant_id",
                table: "reminder_preferences",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminder_preferences_tenant_id_user_id",
                table: "reminder_preferences",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reminder_preferences_user_id",
                table: "reminder_preferences",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminder_preferences");

            migrationBuilder.DropColumn(
                name: "first_reminder_hours",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "second_reminder_hours",
                table: "tenants");
        }
    }
}
