using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrenotaConMe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "google_calendar_sync",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "calendar_event_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_event_links_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "google_calendar_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    google_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    protected_refresh_token = table.Column<string>(type: "text", nullable: false),
                    calendar_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_google_calendar_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_google_calendar_connections_staff_members_staff_member_id",
                        column: x => x.staff_member_id,
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_links_appointment_id",
                table: "calendar_event_links",
                column: "appointment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_links_tenant_id",
                table: "calendar_event_links",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_connections_staff_member_id",
                table: "google_calendar_connections",
                column: "staff_member_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_connections_tenant_id",
                table: "google_calendar_connections",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_event_links");

            migrationBuilder.DropTable(
                name: "google_calendar_connections");

            migrationBuilder.DropColumn(
                name: "google_calendar_sync",
                table: "tenants");
        }
    }
}
