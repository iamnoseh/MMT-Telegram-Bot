namespace TelegramBot.Constants;


public static class BotConstants
{
    
    public const int MaxQuestions = 10;
    public const int QuestionTimeLimit = 30; 
    public const int MaxDuelRounds = 10;
    public const int BaseScore = 10;
    public const int SpeedBonus = 2;
    public const int ReferralBonus = 5;
    public const int CorrectAnswerScore = 1;
    public const string BotUsername = "darsnet_bot";
    
    public static readonly HashSet<int> NoTimerSubjects = new() 
    { 
        1,  
        8,  
        10  
    };
    
    public const long MaxFileSize = 50 * 1024 * 1024; 
    
    public static readonly string[] AcceptedDocumentFormats = { ".docx", ".doc" };
    public static readonly string[] AcceptedBookFormats = { ".pdf", ".epub", ".djvu", ".doc", ".docx" };
}
