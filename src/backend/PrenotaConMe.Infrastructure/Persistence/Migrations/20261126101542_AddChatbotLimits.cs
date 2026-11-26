using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrenotaConMe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatbotLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "rate_day",
                table: "chat_conversations",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "rate_day_count",
                table: "chat_conversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rate_minute_count",
                table: "chat_conversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rate_minute_start",
                table: "chat_conversations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateTable(
                name: "chat_usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    messages = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    audio_seconds = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_usage", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_usage_tenant_id",
                table: "chat_usage",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_usage_tenant_id_day",
                table: "chat_usage",
                columns: new[] { "tenant_id", "day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_usage");

            migrationBuilder.DropColumn(
                name: "rate_day",
                table: "chat_conversations");

            migrationBuilder.DropColumn(
                name: "rate_day_count",
                table: "chat_conversations");

            migrationBuilder.DropColumn(
                name: "rate_minute_count",
                table: "chat_conversations");

            migrationBuilder.DropColumn(
                name: "rate_minute_start",
                table: "chat_conversations");
        }
    }
}
