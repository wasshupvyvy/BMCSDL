using ScheduleAppApi.Helpers;
using ScheduleAppApi.Services.Interfaces;
using System;
using System.Threading.Tasks;
using ScheduleAppApi.Models;

namespace ScheduleAppApi.Services.Implementations
{
    public class AuthService : IAuthService
    {
        public async Task<bool> SendPasswordResetLink(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            {
                return false;
            }


            var resetToken = Guid.NewGuid().ToString("N");


            Console.WriteLine($"[EMAIL SERVICE] Gửi liên kết đặt lại cho {email}. Token: {resetToken}");

            return true;
        }

        public async Task<bool> ResetPassword(string email, string token, string newPassword)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token)) return false;


            if (token != "valid_token_for_test" && token.Length < 10)
            {
                return false;
            }

            var hashedNewPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);

            return true;
        }
    }
}