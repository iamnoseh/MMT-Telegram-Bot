namespace MMT.TelegramBot.Configuration;

public class BotConfiguration
{
    public const string SectionName = "BotConfiguration";
    
    public string Token { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelLink { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}
