using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Chatbot.Domain;

namespace PrenotaConMe.Modules.Chatbot.Persistence;

internal sealed class ChatUsageConfiguration : IEntityTypeConfiguration<ChatUsage>
{
    public void Configure(EntityTypeBuilder<ChatUsage> builder)
    {
        builder.ToTable("chat_usage");
        builder.Ignore(u => u.Tokens);
        // Target of the INSERT ... ON CONFLICT in ChatbotEngine.
        builder.HasIndex(u => new { u.TenantId, u.Day }).IsUnique();
    }
}
