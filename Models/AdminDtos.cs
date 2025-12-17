using System;

namespace ScheduleAppApi.Models
{

    public class UserDto
    {
        public long Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "USER";
        public bool IsLocked { get; set; }
        public int FailedCount { get; set; }
    }


    public class AuditLogDto
    {
        public string Time { get; set; } = string.Empty;
        public string User { get; set; } = "Khách";
        public string Action { get; set; } = string.Empty;
        public string Object { get; set; } = string.Empty;
        public string Result { get; set; } = "Thành công";
    }
    public class AdminScheduleDto
    {
        public long Id { get; set; }
        public string Username { get; set; } = string.Empty; 
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty; 
        public string TimeRange { get; set; } = string.Empty; 
    }
}