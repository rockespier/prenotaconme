using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrenotaConMe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatHandoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "handoff_at",
                table: "chat_conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "handoff_reason",
                table: "chat_conversations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "handoff_at",
                table: "chat_conversations");

            migrationBuilder.DropColumn(
                name: "handoff_reason",
                table: "chat_conversations");
        }
    }
}
