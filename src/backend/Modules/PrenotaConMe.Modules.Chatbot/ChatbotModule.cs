using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PrenotaConMe.Shared.Chatbot;
using PrenotaConMe.Shared.Modules;

namespace PrenotaConMe.Modules.Chatbot;

/// <summary>
/// F3-03/F3-04: booking assistant on chat channels, by text or voice (OpenAI function calling over the Scheduling booking rules).
/// Channel-agnostic: channels (Telegram now, WhatsApp later) call <see cref="IChatbot"/>. Depends on Scheduling.
/// </summary>
public sealed class ChatbotModule : IModule
{
    public string Name => "chatbot";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ChatbotOptions>(configuration.GetSection("OpenAI"));
        services.Configure<ChatbotLimits>(configuration.GetSection("Chatbot"));
        services.AddSingleton<ChatbotMetrics>();
        services.AddSingleton<IChatModel>(sp => new OpenAiChatModel(sp.GetRequiredService<IOptions<ChatbotOptions>>().Value));
        services.AddSingleton<ISpeechToText>(sp => new OpenAiSpeechToText(sp.GetRequiredService<IOptions<ChatbotOptions>>().Value));
        services.AddScoped<BookingTools>();
        services.AddScoped<IChatbot, ChatbotEngine>();

        // F5-04: export of the user's chats and daily deletion of old ones.
        services.Configure<PrivacyOptions>(configuration.GetSection("Privacy"));
        services.AddScoped<PrenotaConMe.Shared.Privacy.IPersonalData, ChatbotPersonalData>();
        services.AddSingleton<PrenotaConMe.Infrastructure.Jobs.IRecurringJobRegistration, ChatRetentionRegistration>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => HandoffEndpoints.Map(endpoints);
}
