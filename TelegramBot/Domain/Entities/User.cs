namespace TelegramBot.Domain.Entities;

    public class User
    {
        public int Id { get; set; }
        public long ChatId { get; set; }
        public string Name { get; set; }
        public string PhoneNumber { get; set; }
        public string City { get; set; }
        public string Username { get; set; }
        public int Score { get; set; }
    }
