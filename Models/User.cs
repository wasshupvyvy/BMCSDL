namespace ScheduleAppApi.Models
{
    public class User
    {
        public int UserId { get; set; }

        public string Email { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public string Role { get; set; } = "Client";

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}