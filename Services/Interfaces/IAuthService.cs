using ScheduleAppApi.Models;
using System.Threading.Tasks;

namespace ScheduleAppApi.Services.Interfaces
{
    public interface IAuthService
    {
        
        Task<bool> SendPasswordResetLink(string email);
        Task<bool> ResetPassword(string email, string token, string newPassword);
    }
}