namespace MMT.Domain.Constants;


public static class Messages
{
    #region Сабти Ном (Registration)
    
    public const string WelcomeMessage = "Хуш омадед! Барои оғози тест тугмаи 'Оғози тест'-ро пахш кунед.";
    public const string RegistrationSuccess = "Сабти номи шумо бо муваффақият анҷом ёфт!\nБарои оғози тест тугмаи 'Оғози тест'-ро пахш кунед!";
    public const string AlreadyRegistered = "Шумо аллакай ба қайд гирифта шудаед.";
    public const string SharePhoneRequest = "Барои сабти ном тугмаи зеринро пахш кунед ва рақами телефони худро фиристед!";
    public const string ThankYouEnterName = "Ташаккур! Акнун номатонро ворид кунед.";
    public const string EnterCity = "Лутфан, шаҳратонро ворид кунед.";
    public const string FirstSharePhone = "Лутфан, аввал рақами телефони худро фиристед!";
    public const string RegistrationError = "Хатогӣ ҳангоми сабти маълумот рух дод. Лутфан, баъдтар дубора кӯшиш кунед.";
    
    #endregion

    #region Хатогиҳо (Errors)
    
    public const string InvalidCommand = "Фармони нодуруст!";
    public const string ErrorOccurred = "Хатогӣ рух дод. Лутфан, баъдтар дубора кӯшиш кунед.";
    public const string ChannelNotFound = "ID-и канал ёфт нашуд!";
    public const string ChannelLinkNotFound = "Пайванди канал ёфт нашуд!";
    public const string BotTokenNotConfigured = "Токени бот танзим нашудааст!";
    
    #endregion

    #region Обуна (Subscription)
    
    public const string SubscribeToChannel = "⚠️ Барои истифодаи бот, аввал ба канали мо обуна шавед!\n\nПас аз обуна шудан, тугмаи '🔄 Санҷиш'-ро пахш кунед.";
    public const string SubscribeButton = "Обуна шудан ба канал";
    public const string CheckButton = "🔄 Санҷиш";
    public const string RestartButton = "🔄 Аз нав оғоз кардан";
    public const string AccountBlocked = "⚠️ Мутаассифона, ҳисоби шумо дастрас нест ё баста шудааст. Лутфан, ботро аз нав оғоз кунед.\n\nТугмаи '🔄 Аз нав оғоз кардан'-ро пахш кунед ё фармони /start-ро фиристед.";
    public const string SubscriptionCheckError = "❌ Хатогӣ ҳангоми санҷиши обунаи шумо рух дод. Лутфан, баъдтар кӯшиш кунед.";
    
    #endregion

    #region Тест (Test)
    
    public const string SelectSubjectFirst = "❌ Лутфан, аввал фанро интихоб кунед!";
    public const string TestFinished = "<b>📝 Тест ба охир расид!</b>\nХолҳои шумо: {0}/{1}.";
    public const string NoQuestionsAvailable = "❌ Дар айни замон саволҳо барои ин фан дастрас нестанд.";
    public const string QuestionNotFound = "Савол ёфт нашуд.";
    public const string RestartTest = "♻️ Аз нав оғоз кунед!";
    public const string SubjectSelected = "Шумо фани {0}-ро интихоб кардед.\nБарои оғози тест тугмаи 'Оғози тест'-ро пахш кунед.";
    public const string SubjectSelectedAdmin = "Шумо фани {0}-ро интихоб кардед.\nБарои илова кардани саволҳо файли .docx фиристед.";
    public const string BackToMain = "Бозгашт ба менюи асосӣ";
    public const string SelectSubject = "Лутфан, фанро интихоб кунед:";
    
    #endregion

    #region Профил (Profile)
    
    public const string NameChanged = "Номи шумо ба '{0}' иваз шуд!";
    public const string AlreadyChangedName = "Шумо аллакай як бор номи худро иваз кардаед.";
    public const string EnterNewName = "Лутфан, номи нави худро ворид кунед:";
    
    #endregion

    #region Админ (Admin)
    
    public const string OnlyAdminsCanUpload = "❌ Танҳо админҳо метавонанд файл бор кунанд!";
    public const string OnlyAdminsCanBroadcast = "❌ Танҳо админҳо метавонанд паём фиристанд!";
    public const string OnlyAdminsCanUploadBooks = "❌ Танҳо админҳо метавонанд китоб илова кунанд!";
    public const string BroadcastPrompt = "📢 Лутфан, паёмеро, ки ба ҳамаи корбарон фиристода мешавад, ворид кунед:";
    public const string BroadcastCancelled = "Фиристодани паём бекор карда шуд!";
    public const string CancelButton = "❌ Бекор кардан";
    
    #endregion

    #region Китобхона (Library)
    
    public const string EnterBookTitle = "📚 Лутфан, номи китобро ворид кунед:";
    public const string EnterBookDescription = "📝 Лутфан, тавсифи китобро ворид кунед:";
    public const string EnterBookYear = "📅 Лутфан, соли нашри китобро ворид кунед:";
    public const string InvalidYear = "❌ Соли нашр бояд рақам бошад!";
    public const string SendBookFile = "✅ Акнун файли китобро фиристед.";
    public const string FillBookInfoFirst = "❌ Лутфан, аввал маълумоти китобро пурра ворид кунед!";
    public const string CannotBeEmpty = "❌ Маълумот наметавонад холӣ бошад!";
    
    #endregion

    #region Мусобиқа (Duel)
    
    public const string CannotInviteSelf = "❌ Шумо наметавонед худатонро даъват кунед!";
    public const string DuelAccepted = "🎮 Шумо даъватро қабул кардед! Бозӣ оғоз шуд!";
    public const string DuelAcceptedForInviter = "🎮 Бозингар даъвати шуморо қабул кард! Бозӣ оғоз шуд!";
    public const string DuelRejected = "❌ Бозингар даъвати шуморо рад кард.";
    public const string DuelSubjectNotFound = "❌ Хатогӣ: Фан ёфт нашуд!";
    
    #endregion

    #region Даъвати Дӯстон (Referral)
    
    public const string ReferralBonus = "🎉 Дӯсти шумо бо пайванди даъват сабти ном шуд! Шумо 5 бал гирифтед!";
    public const string ContactAdmin = "Барои фиристодани савол ё дархост ба админ, ба ин суроға муроҷиат кунед:";
    public const string ContactAdminButton = "💬 Тамос бо админ";
    
    #endregion

    #region Тугмаҳои асосӣ (Main Buttons)
    
    public const string ButtonStartTest = "🎯 Оғози тест";
    public const string ButtonSelectSubject = "📚 Интихоби фан";
    public const string ButtonTopUsers = "🏆 Беҳтаринҳо";
    public const string ButtonProfile = "👤 Профил";
    public const string ButtonDuel = "🎮 Мусобиқа";
    public const string ButtonContactAdmin = "💬 Тамос бо админ";
    public const string ButtonInviteFriends = "👥 Даъвати дӯстон";
    public const string ButtonHelp = "ℹ️ Кӯмак";
    public const string ButtonLibrary = "📚 Китобхона";
    public const string ButtonStatistics = "📊 Омор";
    public const string ButtonAdmin = "👨‍💼 Админ";
    public const string ButtonChangeName = "✏️ Ивази ном";
    public const string ButtonBack = "⬅️ Бозгашт";
    public const string ButtonUploadFile = "📤 Боркунии файл";
    public const string ButtonBroadcast = "📢 Фиристодани паём";
    public const string ButtonUploadBook = "📤 Иловаи китоб";
    
    #endregion

    #region Фанҳо (Subjects)
    
    public const string SubjectChemistry = "🧪 Химия";
    public const string SubjectBiology = "🔬 Биология";
    public const string SubjectTajik = "📖 Забони тоҷикӣ";
    public const string SubjectEnglish = "🌍 Забони англисӣ";
    public const string SubjectHistory = "📜 Таърих";
    public const string SubjectGeography = "🌍 География";
    public const string SubjectLiterature = "📚 Адабиёти тоҷик";
    public const string SubjectPhysics = "⚛️ Физика";
    public const string SubjectRussian = "🇷🇺 Забони русӣ";
    public const string SubjectMath = "📐 Математика";
    public const string SubjectAnatomy = "🫀 Анатомия";
    public const string SubjectHumanRights = "⚖️ Ҳуқуқи инсон";
    public const string SubjectGenetics = "🧬 Генетика";

    #endregion

    #region Логҳо (Logs)
    
    public const string UserRegistered = "Корбар бо муваффақият сабт шуд: ChatId={0}, Name={1}";
    public const string BotStarted = "Бот бо номи {0} пайваст шуд";
    public const string InvalidUserCleaned = "✅ {0} корбари нодуруст аз пойгоҳи додаҳо нест карда шуд";
    public const string UserMarkedAsLeft = "Корбари нодуруст ёфт шуд: {0} - {1}";
    public const string DatabaseMigrationSuccess = "Database migration and seeding completed successfully";
    public const string DatabaseMigrationError = "Error during database migration or seeding";
    public const string CleanupError = "Хатогӣ ҳангоми тозакунии корбарони нодуруст: {0}";
    
    #endregion
}
