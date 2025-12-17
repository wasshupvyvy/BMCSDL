using System;

namespace ScheduleApp.Models
{
    public class Schedule
    {
        public int ScheduleId { get; set; }

        public int UserId { get; set; }

        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }

        public DateTimeOffset StartTime { get; set; }
        public DateTimeOffset EndTime { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}