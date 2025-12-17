using System;
using System.Text.Json.Serialization;

namespace ScheduleAppApi.Models
{
    public record CreateScheduleDto(
        string Title,
        string Description,

        [property: JsonPropertyName("startTime")]
        DateTimeOffset StartTime,

        [property: JsonPropertyName("endTime")]
        DateTimeOffset EndTime,

        int SecurityLabelId = 1
    );

    public record UpdateScheduleDto(
        string Title,
        string Description,

        [property: JsonPropertyName("startTime")]
        DateTimeOffset StartTime,

        [property: JsonPropertyName("endTime")]
        DateTimeOffset EndTime
    );

    public record ScheduleDto(
        long ScheduleId,
        string Title,
        string Description,
        DateTimeOffset StartTime,
        DateTimeOffset EndTime,
        int SecurityLabelId
    );
}