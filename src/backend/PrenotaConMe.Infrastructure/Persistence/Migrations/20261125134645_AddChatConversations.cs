using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrenotaConMe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    messages = table.Column<string>(type: "jsonb", nullable: false),
                    message_count = table.Column<int>(type: "integer", nullable: false),
                    pending_action = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    pending_at = table.Column<int>(type: "integer", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_conversations", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_conversations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_conversations_tenant_id",
                table: "chat_conversations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_conversations_tenant_id_channel_conversation_id",
                table: "chat_conversations",
                columns: new[] { "tenant_id", "channel", "conversation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_conversations_user_id",
                table: "chat_conversations",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_conversations");
        }
    }
}
