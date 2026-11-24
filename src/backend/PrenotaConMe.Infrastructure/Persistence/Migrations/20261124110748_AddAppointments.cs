using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrenotaConMe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    service_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.CheckConstraint("ck_appointments_ends_after_start", "ends_at > starts_at");
                    table.ForeignKey(
                        name: "fk_appointments_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_staff_members_staff_member_id",
                        column: x => x.staff_member_id,
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_client_user_id",
                table: "appointments",
                column: "client_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_location_id_starts_at",
                table: "appointments",
                columns: new[] { "location_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_service_id",
                table: "appointments",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_staff_member_id_starts_at",
                table: "appointments",
                columns: new[] { "staff_member_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_tenant_id",
                table: "appointments",
                column: "tenant_id");

            // F1-09: a staff member can never have two overlapping, non-cancelled appointments, even under
            // concurrent bookings. Half-open ranges [start, end) so back-to-back appointments are allowed.
            // Not modelled by EF: keep the name in sync with AppointmentConfiguration.NoOverlapConstraint.
            migrationBuilder.Sql("""
                ALTER TABLE appointments
                ADD CONSTRAINT ex_appointments_staff_no_overlap
                EXCLUDE USING gist (
                    staff_member_id WITH =,
                    tstzrange(starts_at, ends_at, '[)') WITH &&
                )
                WHERE (status <> 'Cancelled');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointments");
        }
    }
}
