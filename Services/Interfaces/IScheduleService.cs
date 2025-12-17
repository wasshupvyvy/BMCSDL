using ScheduleAppApi.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ScheduleAppApi.Services
{
    public interface IScheduleService
    {
        Task<int> CreateScheduleAsync(CreateScheduleDto dto, int userId);

        Task<IEnumerable<ScheduleDto>> GetMySchedulesAsync(int userId);

        Task<bool> UpdateScheduleAsync(int scheduleId, UpdateScheduleDto dto, int userId);

        Task<bool> DeleteScheduleAsync(int scheduleId, int userId);

        Task<IEnumerable<ScheduleDto>> GetAllSchedulesAsync();
    }
}