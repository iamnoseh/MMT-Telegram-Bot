using Telegram.Bot.Types;

namespace TelegramBot.Domain.DTOs;

public class RegistrationInfo
{
    public Contact Contact { get; set; }
    public string AutoUsername { get; set; }
    public string Name { get; set; }
    public string City { get; set; }
    public bool IsNameReceived { get; set; }
    public bool IsCityReceived { get; set; }
}