using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Chatbot.Domain;
using PrenotaConMe.Modules.Users.Domain;

namespace PrenotaConMe.Modules.Chatbot.Persistence;

internal sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("chat_conversations");
        builder.Property(c => c.Channel).HasMaxLength(ChatConversation.ChannelMaxLength).IsRequired();
        builder.Property(c => c.ConversationId).HasMaxLength(ChatConversation.ConversationIdMaxLength).IsRequired();
        builder.Property(c => c.Messages).HasColumnType("jsonb").IsRequired();
        // Plain text, not jsonb: it is compared as written.
        builder.Property(c => c.PendingAction).HasMaxLength(ChatConversation.PendingActionMaxLength);
        builder.Property(c => c.HandoffReason).HasMaxLength(ChatConversation.HandoffReasonMaxLength);
        builder.HasIndex(c => new { c.TenantId, c.Channel, c.ConversationId }).IsUnique();
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
