using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBot.Domain.Entities;
using TelegramBot.Services.OptionServices;
using TelegramBot.Services.QuestionServise;
using TelegramBot.Services.UserResponceService;
using User = TelegramBot.Domain.Entities.User;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DataContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<IOptionService, OptionService>();
builder.Services.AddScoped<IResponseService, ResponseService>();

await using var app = builder.Build();

app.MapGet("/questions", async (DataContext context) =>
{
    var questions = await context.Questions.Include(q => q.Option).ToListAsync();
    return Results.Ok(questions);
});





var token = builder.Configuration["BotConfiguration:Token"]
    ?? throw new ArgumentNullException("Telegram Bot Token is not configured!");

var botHelper = new TelegramBotHelper(
    token,
    app.Services.GetRequiredService<IQuestionService>(),
    app.Services.GetRequiredService<IOptionService>(),
    app.Services.GetRequiredService<IResponseService>(),
    app.Services);

Task.Run(() => botHelper.StartBotAsync());

app.Run();
#region TelegramBotHelper

internal class TelegramBotHelper
{
    private readonly IOptionService _optionService;
    private readonly IQuestionService _service;
    private readonly IResponseService _responseService;
    private readonly TelegramBotClient _client;
    private readonly IServiceProvider _serviceProvider;

//from questions
    private readonly Dictionary<long, int> _userScores = new();
    private readonly Dictionary<long, int> _userQuestions = new();
    private const int MaxQuestions = 10;
//from user
    private readonly Dictionary<long, RegistrationInfo> _pendingRegistrations = new();

    public TelegramBotHelper(
        string token,
        IQuestionService questionService,
        IOptionService optionService,
        IResponseService responseService,
        IServiceProvider serviceProvider)
    {
        _service = questionService;
        _optionService = optionService;
        _responseService = responseService;
        _client = new TelegramBotClient(token);
        _serviceProvider = serviceProvider;
    }

    internal async Task StartBotAsync()
    {
        try
        {
            var me = await _client.GetMeAsync();
            if (me == null)
            {
                Console.WriteLine("Failed to retrieve bot details!");
                return;
            }
            Console.WriteLine($"Bot connected as: {me.Username}");
            
            var offset = 0;
            while (true)
            {
                var updates = await _client.GetUpdatesAsync(offset);
                foreach (var update in updates)
                {
                    await HandleUpdateAsync(update);
                    offset = update.Id + 1;
                }
                Thread.Sleep(1000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting bot: {ex.Message}");
        }
    }

    private async Task HandleUpdateAsync(Update update)
    {
        if (update.Type == UpdateType.Message && update.Message != null)
        {
            var chatId = update.Message.Chat.Id;
            var text = update.Message.Text;
            Console.WriteLine($"Received message from Chat ID: {chatId}");
            
            if (update.Message.Contact != null)
            {
                await HandleContactRegistration(update.Message);
                return;
            }
            
            if (_pendingRegistrations.ContainsKey(chatId))
            {
                var reg = _pendingRegistrations[chatId];
                if (!reg.IsNameReceived)
                {
                    await HandleNameRegistration(chatId, text);
                    return;
                }
                else if (reg.IsNameReceived && !reg.IsCityReceived)
                {
                    await HandleCityRegistration(chatId, text);
                    return;
                }
            }

            // Калидҳои фармонҳо:
            switch (text)
            {
                case "/start":
                    if (!await IsUserRegistered(chatId))
                    {
                        await SendRegistrationRequest(chatId);
                    }
                    else
                    {
                        await _client.SendTextMessageAsync(chatId,
                            "Хуш омадед! Барои оғоз тугмаи 'Саволи нав'-ро пахш кунед.",
                            replyMarkup: GetMainButtons());
                    }
                    break;

                case "/register":
                    if (!await IsUserRegistered(chatId))
                    {
                        await SendRegistrationRequest(chatId);
                    }
                    else
                    {
                        await _client.SendTextMessageAsync(chatId,
                            "Шумо аллакай сабт шудаед!");
                    }
                    break;

                case "Саволи нав":
                    if (!await IsUserRegistered(chatId))
                    {
                        await _client.SendTextMessageAsync(chatId,
                            "Лутфан аввал ба бот сабт шавед. Барои сабт кардани худ /register-ро пахш кунед.");
                    }
                    else
                    {
                        await HandleNewQuestion(chatId);
                    }
                    break;

                case "Top":
                    await HandleTopCommand(chatId);
                    break;

                case "Profile":
                    await HandleProfileCommand(chatId);
                    break;

                case "Help":
                    await HandleHelpCommand(chatId);
                    break;

                default:
                    if (_pendingRegistrations.ContainsKey(chatId))
                    {
                        var regInfo = _pendingRegistrations[chatId];
                        if (!regInfo.IsNameReceived)
                        {
                            await HandleNameRegistration(chatId, text);
                            return;
                        }
                        if (regInfo.IsNameReceived && !regInfo.IsCityReceived)
                        {
                            await HandleCityRegistration(chatId, text);
                            return;
                        }
                    }
                    break;
            }
        }
        else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            await HandleCallbackQuery(update.CallbackQuery);
        }
    }
    
    private async Task<bool> IsUserRegistered(long chatId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        return await dbContext.Users.AnyAsync(u => u.ChatId == chatId);
    }

    #region Registration Methods

    private async Task SendRegistrationRequest(long chatId)
    {
        var requestContactButton = new KeyboardButton("Пешкаш кардани тамос") { RequestContact = true };
        var keyboard = new ReplyKeyboardMarkup(new List<KeyboardButton> { requestContactButton })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        await _client.SendTextMessageAsync(chatId,
            "Барои сабт кардан, лутфан тугмаи зер пахш кунед ва рақами телефони худро фиристед.",
            replyMarkup: keyboard);
    }

    private async Task HandleContactRegistration(Message message)
    {
        var chatId = message.Chat.Id;
        var contact = message.Contact;
        
        // Агар message.Chat.Username холӣ бошад, аз FirstName ҳамчун арзиши муфид истифода мекунем.
        var autoUsername = !string.IsNullOrWhiteSpace(message.Chat.Username)
                                ? message.Chat.Username
                                : message.Chat.FirstName;

        if (!_pendingRegistrations.ContainsKey(chatId))
        {
            _pendingRegistrations[chatId] = new RegistrationInfo
            {
                Contact = contact,
                AutoUsername = autoUsername,
                IsNameReceived = false,
                IsCityReceived = false
            };

            await _client.SendTextMessageAsync(chatId,
                "Ташаккур! Акнун, лутфан номи худро (Name) ворид кунед.",
                replyMarkup: new ReplyKeyboardRemove());
        }
        else
        {
            await _client.SendTextMessageAsync(chatId,
                "Лутфан номи худро ворид кунед, то сабт ба итмом расад.");
        }
    }

    private async Task HandleNameRegistration(long chatId, string name)
    {
        if (!_pendingRegistrations.ContainsKey(chatId))
            return;

        var regInfo = _pendingRegistrations[chatId];
        regInfo.Name = name;
        regInfo.IsNameReceived = true;

        // Пас аз гирифтани номи корбар, хабар диҳем, ки акнун барои сабт намудани шаҳр интизор шавед.
        await _client.SendTextMessageAsync(chatId,
            "Лутфан шаҳри худро ворид кунед.",
            replyMarkup: new ReplyKeyboardRemove());
    }

    private async Task HandleCityRegistration(long chatId, string city)
    {
        if (!_pendingRegistrations.ContainsKey(chatId))
            return;

        var regInfo = _pendingRegistrations[chatId];
        regInfo.City = city;
        regInfo.IsCityReceived = true;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

            var user = new User
            {
                ChatId = chatId,
                Username = regInfo.AutoUsername,
                Name = regInfo.Name,
                PhoneNumber = regInfo.Contact.PhoneNumber,
                City = regInfo.City,
                Score = 0 // Инициализатсияи асосии Score
            };

            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();

            await _client.SendTextMessageAsync(chatId,
                "Сабти шумо бо муваффақият анҷом ёфт!\nҲоло шумо метавонед ба функсияҳои дигар, масалан 'Саволи нав', 'Top', 'Profile' ва 'Help', дастрасӣ пайдо кунед.",
                replyMarkup: GetMainButtons());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving user registration: {ex.Message}");
            await _client.SendTextMessageAsync(chatId,
                "Дар сабти маълумот хатое рӯй дод. Лутфан баъдтар кӯшиш намоед.");
        }
        finally
        {
            _pendingRegistrations.Remove(chatId);
        }
    }

    #endregion

    #region Question Methods
    
    private IReplyMarkup GetMainButtons()
    {
        return new ReplyKeyboardMarkup
        {
            Keyboard = new List<List<KeyboardButton>>
            {
                new() { new KeyboardButton("Саволи нав"), new KeyboardButton("Top") },
                new() { new KeyboardButton("Profile"), new KeyboardButton("Help") }
            },
            ResizeKeyboard = true
        };
    }

    private IReplyMarkup GetButtons(int questionId)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("A", $"{questionId}_A"),
                InlineKeyboardButton.WithCallbackData("B", $"{questionId}_B")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("C", $"{questionId}_C"),
                InlineKeyboardButton.WithCallbackData("D", $"{questionId}_D")
            }
        });
    }

    private async Task HandleNewQuestion(long chatId)
    {
        if (!_userQuestions.ContainsKey(chatId))
        {
            _userQuestions[chatId] = 0;
            _userScores[chatId] = 0;
        }

        if (_userQuestions[chatId] >= MaxQuestions)
        {
            string res;
            if (_userScores[chatId] == MaxQuestions)
            {
                res = $"Оффарин! Шумо 100% холҳои имконпазирро соҳиб шудед!\nХолҳои шумо: {_userScores[chatId]}/{MaxQuestions}.";
            }
            else
            {
                res = $"Бозии шумо ба охир расид!\nХолҳои шумо: {_userScores[chatId]}/{MaxQuestions}.\nАз нав кӯшиш кунед!";
            }
            await _client.SendTextMessageAsync(chatId, res,
                replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Аз нав оғоз кардан !", "restart")));
            return;
        }

        var question = await _service.GetQuestionWithOptionsDTO();
        if (question != null)
        {
            _userQuestions[chatId]++;
            await _client.SendTextMessageAsync(chatId,
                $"{question.QuestionText}\nA) {question.FirstOption}\nB) {question.SecondOption}\nC) {question.ThirdOption}\nD) {question.FourthOption}",
                replyMarkup: GetButtons(question.QuestionId));
        }
        else
        {
            await _client.SendTextMessageAsync(chatId, "Дар айни замон дастрасии саволҳо имконнопазир нест.");
        }
    }

    private async Task HandleCallbackQuery(CallbackQuery callbackQuery)
    {
        var chatId = callbackQuery.Message.Chat.Id;
        var messageId = callbackQuery.Message.MessageId;
        var callbackData = callbackQuery.Data.Split('_');

        if (callbackQuery.Data == "restart")
        {
            _userScores[chatId] = 0;
            _userQuestions[chatId] = 0;

            await _client.EditMessageTextAsync(chatId, messageId, "Бозии шумо аз нав оғоз шуд! Барои оғоз \"Саволи нав\"-ро пахш кунед.");
            return;
        }

        if (!int.TryParse(callbackData[0], out int questionId))
            return;

        var question = await _service.GetQuestionById(questionId);
        if (question == null)
        {
            await _client.EditMessageTextAsync(chatId, messageId, "Савол ёфт нашуд.");
            return;
        }

        if (!_userScores.ContainsKey(chatId))
        {
            _userScores[chatId] = 0;
        }

        var selectedOption = callbackData[1].Trim().ToUpper();
        string correctAnswer = question.Answer.Trim();

        bool isCorrect = selectedOption switch
        {
            "A" => correctAnswer == question.FirstOption.Trim(),
            "B" => correctAnswer == question.SecondOption.Trim(),
            "C" => correctAnswer == question.ThirdOption.Trim(),
            "D" => correctAnswer == question.FourthOption.Trim(),
            _ => false
        };

        if (isCorrect)
        {
            _userScores[chatId]++;
            await _client.EditMessageTextAsync(chatId, messageId, "Офарин! +1 балл");
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
                var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId);
                if (user != null)
                {
                    user.Score += 1;
                    await dbContext.SaveChangesAsync();
                }
            }
        }
        else
        {
            await _client.EditMessageTextAsync(chatId, messageId,
                "Ҷавоби шумо нодуруст аст!\nБарои чавобҳои дурустро дидан метавонед файли саволҳоро боргири кунед ва беҳтар омода шавед!");
        }

        var userResponse = new UserResponse
        {
            ChatId = chatId,
            QuestionId = questionId,
            SelectedOption = selectedOption,
            IsCorrect = isCorrect
        };
        await _responseService.SaveUserResponse(userResponse);
    }

//top 50
    private async Task HandleTopCommand(long chatId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var topUsers = await dbContext.Users.OrderByDescending(u => u.Score).Take(50).ToListAsync();

        if (topUsers.Count == 0)
        {
            await _client.SendTextMessageAsync(chatId, "Ҳеҷ корбар сабт нашудааст.");
            return;
        }

        int cnt = 0;
        var messageText = "Топ 50 корбар:\n#--Ном---Холҳо---Level";
        foreach (var user in topUsers)
        {
            cnt++;
            int level = GetLevel(user.Score);
            messageText += $"№{cnt}--{user.Name} -- {user.Score} -- Левел: {level}\n";
        }
        await _client.SendTextMessageAsync(chatId, messageText);
    }

//profile
    private async Task HandleProfileCommand(long chatId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId);
        if (user != null)
        {
            int level = GetLevel(user.Score);
            string profileText = $"Profile:\nНом: {user.Name}\nШаҳр: {user.City}\nScore: {user.Score}\nЛевел: {level}";
            await _client.SendTextMessageAsync(chatId, profileText);
        }
        else
        {
            await _client.SendTextMessageAsync(chatId, "Шумо ҳанӯз сабт нашудаед. Лутфан барои сабт кардани ҳисоби худ /register-ро истифода баред.");
        }
    }
    
    //help
    private async Task HandleHelpCommand(long chatId)
    {
        string helpText = "Дастурҳо:\n" +
                          "/start - оғоз ва санҷиши сабт шудан\n" +
                          "/register - сабт кардани ҳисоби корбар\n" +
                          "Саволи нав - барои гирифтани савол\n" +
                          "Top - барои дидани топ 50 корбар\n" +
                          "Profile - барои дидани маълумоти шахсии шумо\n" +
                          "Help - барои дидани ин рӯйхат\n";
        await _client.SendTextMessageAsync(chatId, helpText);
    }

    private int GetLevel(int score)
    {
        if (score <= 150)
            return 1;
        else if (score <= 300)
            return 2;
        else if (score <= 450)
            return 3;
        else if (score <= 600)
            return 4;
        else
            return 5;
    }

    #endregion

    #region RegistrationInfo Class

    private class RegistrationInfo
    {
        public Contact Contact { get; set; }
        public string AutoUsername { get; set; }
        public string Name { get; set; }
        public bool IsNameReceived { get; set; }
        public string City { get; set; }
        public bool IsCityReceived { get; set; }
    }

    #endregion
}

#endregion
