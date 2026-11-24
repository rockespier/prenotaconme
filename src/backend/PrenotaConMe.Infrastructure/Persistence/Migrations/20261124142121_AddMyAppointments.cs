using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrenotaConMe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMyAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cancellation_notice_hours",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_notice_hours",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "appointments");
        }
    }
}
