using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Data;
using ScheduleAppApi.Helpers;
using System;
using System.Collections.Generic;

namespace ScheduleAppApi.Controllers
{

    public record UserRegisterDto(string Username, string Email, string Password);
    public record UserLoginDto(string Username, string Password);
    public record ForgotPasswordDto(string Email);
    public record ResetPasswordDto(string Email, string Token, string NewPassword);


    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public long UserId { get; set; }
        public string Role { get; set; } = "USER";
    }

    public class ForgotPasswordResponse
    {
        public string Message { get; set; } = string.Empty;
        public string? ResetToken { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private const string DbSchema = "APPUSER";
        private const int MaxAttempts = 3;
        private const int LockoutDurationMinutes = 10;
        private const int ResetTokenExpiryMinutes = 30;

        public AuthController(IConfiguration config)
        {
            _config = config;
        }


        [HttpPost("register")]
        public IActionResult Register([FromBody] UserRegisterDto dto)
        {

            const string defaultRoleName = "USER";

            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            OracleTransaction? transaction = null;

            try
            {
                conn.Open();
                transaction = conn.BeginTransaction();


                using (var checkCmd = new OracleCommand($"SELECT COUNT(*) FROM {DbSchema}.USERS WHERE USERNAME = :p_username", conn))
                {
                    checkCmd.Transaction = transaction;
                    checkCmd.BindByName = true;
                    checkCmd.Parameters.Add("p_username", dto.Username);
                    var exists = Convert.ToInt32(checkCmd.ExecuteScalar());
                    if (exists > 0)
                    {
                        transaction.Rollback();
                        return BadRequest(new { message = "Tên đăng nhập đã tồn tại." });
                    }
                }


                var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
                long newUserId = 0;


                const string insertUserSql =
                  "INSERT INTO USERS (USERNAME, PASSWORD_HASH, EMAIL, CREATED_AT) " +
                  "VALUES (:p_username, :p_password, :p_email, SYSTIMESTAMP) RETURNING USER_ID INTO :p_new_id";

                using (var cmd = new OracleCommand(insertUserSql, conn))
                {
                    cmd.Transaction = transaction;
                    cmd.BindByName = true;
                    cmd.Parameters.Add("p_username", dto.Username);
                    cmd.Parameters.Add("p_password", passwordHash);

                    if (!string.IsNullOrEmpty(dto.Email))
                    {

                        string encryptedBase64 = SecurityHelper.Encrypt(dto.Email);
                        byte[] emailBytes = Convert.FromBase64String(encryptedBase64);
                        cmd.Parameters.Add("p_email", OracleDbType.Raw).Value = emailBytes;
                    }
                    else
                    {
                        cmd.Parameters.Add("p_email", DBNull.Value);
                    }

                    var idParam = new OracleParameter("p_new_id", OracleDbType.Int32, ParameterDirection.Output);
                    cmd.Parameters.Add(idParam);

                    cmd.ExecuteNonQuery();


                    if (idParam.Value is OracleDecimal oraDec) newUserId = oraDec.ToInt64();
                    else newUserId = Convert.ToInt64(idParam.Value);
                }


                using (var roleIdCmd = new OracleCommand($"SELECT ROLE_ID FROM {DbSchema}.ROLES WHERE ROLE_NAME = :p_role_name", conn))
                {
                    roleIdCmd.Transaction = transaction;
                    roleIdCmd.BindByName = true;
                    roleIdCmd.Parameters.Add("p_role_name", defaultRoleName);
                    var defaultRoleIdObj = roleIdCmd.ExecuteScalar();

                    if (defaultRoleIdObj != null)
                    {
                        int defaultRoleId = Convert.ToInt32(defaultRoleIdObj);
                        using (var assignCmd = new OracleCommand($"INSERT INTO {DbSchema}.USER_ROLES (USER_ID, ROLE_ID) VALUES (:p_user_id, :p_role_id)", conn))
                        {
                            assignCmd.Transaction = transaction;
                            assignCmd.BindByName = true;
                            assignCmd.Parameters.Add("p_user_id", newUserId);
                            assignCmd.Parameters.Add("p_role_id", defaultRoleId);
                            assignCmd.ExecuteNonQuery();
                        }
                    }
                }


                var keys = RsaHelper.GenerateKeys();
                string encryptedPrivateKey = SecurityHelper.Encrypt(keys.PrivateKey);
                string sqlKey = $"INSERT INTO {DbSchema}.USER_KEYS (USER_ID, PUBLIC_KEY, ENCRYPTED_PRIVATE_KEY) VALUES (:val_user_id, :val_public, :val_private)";

                using (var keyCmd = new OracleCommand(sqlKey, conn))
                {
                    keyCmd.Transaction = transaction;
                    keyCmd.BindByName = true;
                    keyCmd.Parameters.Add("val_user_id", newUserId);
                    keyCmd.Parameters.Add("val_public", OracleDbType.Clob).Value = keys.PublicKey;
                    keyCmd.Parameters.Add("val_private", OracleDbType.Clob).Value = encryptedPrivateKey;
                    keyCmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return Ok(new { message = "Đăng ký thành công", userId = newUserId });
            }
            catch (Exception ex)
            {
                transaction?.Rollback();
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
        }


        [HttpPost("login")]
        public IActionResult Login([FromBody] UserLoginDto dto)
        {
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            string? passwordHash = null;
            int userId = 0;
            int failedCount = 0;
            DateTime? lockoutEnd = null;
            string userRole = "USER";

            try
            {
                conn.Open();


                const string selectUserSql = @"
    SELECT u.USER_ID, u.PASSWORD_HASH, u.FAILED_LOGIN_COUNT, u.LOCKOUT_END_DATE, r.ROLE_NAME
    FROM APPUSER.USERS u
    LEFT JOIN APPUSER.USER_ROLES ur ON u.USER_ID = ur.USER_ID
    LEFT JOIN APPUSER.ROLES r ON ur.ROLE_ID = r.ROLE_ID
    WHERE u.USERNAME = :p_username";

                using (var cmd = new OracleCommand(selectUserSql, conn))
                {
                    cmd.BindByName = true;
                    cmd.Parameters.Add("p_username", dto.Username);
                    using var reader = cmd.ExecuteReader();

                    if (reader.Read())
                    {
                        userId = reader.GetInt32(0);
                        passwordHash = reader.GetString(1);
                        failedCount = reader.GetInt32(2);
                        if (!reader.IsDBNull(3)) lockoutEnd = reader.GetDateTime(3);

                        if (!reader.IsDBNull(4)) userRole = reader.GetString(4);
                    }
                }
            }
            catch (Exception ex) { return StatusCode(500, new { message = "Lỗi kết nối CSDL: " + ex.Message }); }

            if (passwordHash == null)
                return Unauthorized(new { message = "Sai tài khoản hoặc mật khẩu" });


            if (lockoutEnd.HasValue && lockoutEnd.Value > DateTime.UtcNow)
            {
                return StatusCode(403, new { message = $"Tài khoản bị khóa đến {lockoutEnd.Value.ToLocalTime():HH:mm:ss}." });
            }


            if (!BCrypt.Net.BCrypt.Verify(dto.Password, passwordHash))
            {

                int newFailedCount = failedCount + 1;
                DateTime? newLockoutEnd = null;
                if (newFailedCount >= MaxAttempts) newLockoutEnd = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Đang cập nhật lỗi cho user {userId}. Số lần sai mới: {newFailedCount}");
                const string updateSql = $"UPDATE {DbSchema}.USERS SET FAILED_LOGIN_COUNT = :p_count, LOCKOUT_END_DATE = :p_lock_date WHERE USER_ID = :p_user_id";
                using (var updateCmd = new OracleCommand(updateSql, conn))
                {
                    updateCmd.BindByName = true;
                    updateCmd.Parameters.Add("p_count", newFailedCount);
                    if (newLockoutEnd.HasValue) updateCmd.Parameters.Add("p_lock_date", OracleDbType.TimeStamp).Value = newLockoutEnd.Value;
                    else updateCmd.Parameters.Add("p_lock_date", DBNull.Value);
                    updateCmd.Parameters.Add("p_user_id", userId);
                    updateCmd.ExecuteNonQuery();
                }
                return Unauthorized(new { message = "Sai tài khoản hoặc mật khẩu" });
            }


            if (failedCount > 0)
            {
                const string resetSql = $"UPDATE {DbSchema}.USERS SET FAILED_LOGIN_COUNT = 0, LOCKOUT_END_DATE = NULL WHERE USER_ID = :p_user_id";
                using (var resetCmd = new OracleCommand(resetSql, conn))
                {
                    resetCmd.BindByName = true;
                    resetCmd.Parameters.Add("p_user_id", userId);
                    resetCmd.ExecuteNonQuery();
                }
            }


            try
            {
                const string setContextSql = "BEGIN APPUSER.PKG_SECURITY.SET_USER_ID(:p_user_id); END;";
                using (var cmdContext = new OracleCommand(setContextSql, conn))
                {
                    cmdContext.Parameters.Add(new OracleParameter("p_user_id", OracleDbType.Int32) { Value = userId });
                    cmdContext.ExecuteNonQuery();
                }
            }
            catch { }


            var key = _config["Jwt:Key"];
            var issuer = _config["Jwt:Issuer"];
            var audience = _config["Jwt:Audience"];

            if (string.IsNullOrEmpty(key) || key.Length < 32) return StatusCode(500, new { message = "Cấu hình JWT Key lỗi." });

            var claims = new List<Claim> {
        new Claim(ClaimTypes.Name, dto.Username),
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        new Claim(ClaimTypes.Role, userRole)
      };

            var token = new JwtSecurityToken(
              issuer, audience, claims,
              expires: DateTime.UtcNow.AddHours(2),
              signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)
            );


            return Ok(new AuthResponse
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                UserId = userId,
                Role = userRole
            });
        }


        [HttpPost("forgot-password")]
        public IActionResult ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email) || !dto.Email.Contains("@"))
                return Ok(new ForgotPasswordResponse { Message = "Yêu cầu đã được xử lý." });

            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            try
            {
                conn.Open();
                string encryptedBase64 = SecurityHelper.Encrypt(dto.Email);
                byte[] emailBytes = Convert.FromBase64String(encryptedBase64);

                const string selectUserIdSql = $"SELECT USER_ID FROM {DbSchema}.USERS WHERE EMAIL = :p_email";
                int userId = 0;
                using (var cmd = new OracleCommand(selectUserIdSql, conn))
                {
                    cmd.BindByName = true;
                    cmd.Parameters.Add("p_email", OracleDbType.Raw).Value = emailBytes;
                    var obj = cmd.ExecuteScalar();
                    if (obj != null) userId = Convert.ToInt32(obj);
                }

                if (userId == 0) return Ok(new ForgotPasswordResponse { Message = "Yêu cầu đã được xử lý." });

                var resetToken = Guid.NewGuid().ToString("N");
                var expiryTime = DateTime.UtcNow.AddMinutes(ResetTokenExpiryMinutes);

                const string updateTokenSql = $"UPDATE {DbSchema}.USERS SET PASSWORD_RESET_TOKEN = :p_token, TOKEN_EXPIRY_TIME = :p_expiry_time WHERE USER_ID = :p_user_id";
                using (var updateCmd = new OracleCommand(updateTokenSql, conn))
                {
                    updateCmd.BindByName = true;
                    updateCmd.Parameters.Add("p_token", resetToken);
                    updateCmd.Parameters.Add("p_expiry_time", OracleDbType.TimeStamp).Value = expiryTime;
                    updateCmd.Parameters.Add("p_user_id", userId);
                    updateCmd.ExecuteNonQuery();
                }
                return Ok(new ForgotPasswordResponse { Message = "Yêu cầu đã được xử lý.", ResetToken = resetToken });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                return Ok(new ForgotPasswordResponse { Message = "Yêu cầu đã được xử lý." });
            }
        }


        [HttpPost("reset-password")]
        public IActionResult ResetPassword([FromBody] ResetPasswordDto dto)
        {
            const string errorMsg = "Token không hợp lệ hoặc đã hết hạn.";
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            try
            {
                conn.Open();
                string encryptedBase64 = SecurityHelper.Encrypt(dto.Email);
                byte[] emailBytes = Convert.FromBase64String(encryptedBase64);

                const string sql = $"SELECT USER_ID, TOKEN_EXPIRY_TIME, PASSWORD_RESET_TOKEN FROM {DbSchema}.USERS WHERE EMAIL = :p_email";
                int userId = 0; DateTime? expiry = null; string? storedToken = null;

                using (var cmd = new OracleCommand(sql, conn))
                {
                    cmd.BindByName = true;
                    cmd.Parameters.Add("p_email", OracleDbType.Raw).Value = emailBytes;
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        userId = reader.GetInt32(0);
                        if (!reader.IsDBNull(1)) expiry = reader.GetDateTime(1);
                        if (!reader.IsDBNull(2)) storedToken = reader.GetString(2);
                    }
                }

                if (userId == 0 || storedToken != dto.Token || !expiry.HasValue || expiry.Value < DateTime.UtcNow)
                {
                    return BadRequest(new { message = errorMsg });
                }

                var newHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
                const string updateSql = $"UPDATE {DbSchema}.USERS SET PASSWORD_HASH = :p_hash, PASSWORD_RESET_TOKEN = NULL, TOKEN_EXPIRY_TIME = NULL, FAILED_LOGIN_COUNT = 0, LOCKOUT_END_DATE = NULL WHERE USER_ID = :p_user_id";

                using (var updateCmd = new OracleCommand(updateSql, conn))
                {
                    updateCmd.BindByName = true;
                    updateCmd.Parameters.Add("p_hash", newHash);
                    updateCmd.Parameters.Add("p_user_id", userId);
                    updateCmd.ExecuteNonQuery();
                }
                return Ok(new { message = "Đặt lại mật khẩu thành công." });
            }
            catch { return StatusCode(500, new { message = "Lỗi server." }); }
        }
    }
}