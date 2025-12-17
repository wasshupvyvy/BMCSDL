using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using ScheduleAppApi.Helpers;
using ScheduleAppApi.Models;
using System.Data;

namespace ScheduleAppApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly IConfiguration _config;
        private const string DbSchema = "APPUSER";

        public AdminController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = new List<UserDto>();
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));

            try
            {
                await conn.OpenAsync();

                string sql = $@"
                    SELECT u.USER_ID, u.USERNAME, u.EMAIL, r.ROLE_NAME, u.LOCKOUT_END_DATE, u.FAILED_LOGIN_COUNT
                    FROM {DbSchema}.USERS u
                    LEFT JOIN {DbSchema}.USER_ROLES ur ON u.USER_ID = ur.USER_ID
                    LEFT JOIN {DbSchema}.ROLES r ON ur.ROLE_ID = r.ROLE_ID
                    ORDER BY u.USER_ID DESC";

                using var cmd = new OracleCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    string emailDisplay = "N/A";
                    if (!reader.IsDBNull(2))
                    {
                        try
                        {
                            byte[] emailBytes = (byte[])reader.GetValue(2);
                            string base64Email = Convert.ToBase64String(emailBytes);
                            emailDisplay = SecurityHelper.Decrypt(base64Email);
                        }
                        catch { emailDisplay = "[Encrypted]"; }
                    }

                    bool isLocked = false;
                    if (!reader.IsDBNull(4))
                    {
                        var lockoutDate = reader.GetDateTime(4);
                        if (lockoutDate > DateTime.UtcNow) isLocked = true;
                    }

                    int failedCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);

                    users.Add(new UserDto
                    {
                        Id = reader.GetInt64(0),
                        Username = reader.GetString(1),
                        Email = emailDisplay,
                        Role = reader.IsDBNull(3) ? "USER" : reader.GetString(3),
                        IsLocked = isLocked,
                        FailedCount = failedCount
                    });
                }

                return Ok(users);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error: " + ex.Message });
            }
        }

        [HttpGet("audit-logs")]
        public async Task<IActionResult> GetAuditLogs()
        {
            var logs = new List<AuditLogDto>();
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));

            try
            {
                await conn.OpenAsync();
                string sql = $@"
                    SELECT a.ACTION_TIME, u.USERNAME, a.ACTION, a.OBJECT_TYPE, a.OBJECT_ID
                    FROM {DbSchema}.AUDIT_LOGS a
                    LEFT JOIN {DbSchema}.USERS u ON a.USER_ID = u.USER_ID
                    ORDER BY a.ACTION_TIME DESC
                    FETCH FIRST 50 ROWS ONLY";

                using var cmd = new OracleCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    string action = reader.IsDBNull(2) ? "" : reader.GetString(2);

                    string result = "Success";
                    if (action.Contains("FAILURE") || action.Contains("DENIED")) result = "Failed";

                    logs.Add(new AuditLogDto
                    {
                        Time = reader.GetDateTime(0).ToString("dd/MM/yyyy HH:mm:ss"),
                        User = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                        Action = action,
                        Object = reader.IsDBNull(3) ? "-" : reader.GetString(3),
                        Result = result
                    });
                }
                return Ok(logs);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error: " + ex.Message });
            }
        }

        [HttpPost("users/{id}/lock")]
        public async Task<IActionResult> LockUser(long id)
        {
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            try
            {
                await conn.OpenAsync();

                var permanentLockDate = new DateTime(9999, 12, 31, 23, 59, 59);

                string sql = $"UPDATE {DbSchema}.USERS SET LOCKOUT_END_DATE = :p_date WHERE USER_ID = :p_id";

                using var cmd = new OracleCommand(sql, conn);
                cmd.Parameters.Add("p_date", OracleDbType.TimeStamp).Value = permanentLockDate;
                cmd.Parameters.Add("p_id", id);
                await cmd.ExecuteNonQueryAsync();

                return Ok(new { message = "Locked successfully." });
            }
            catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }
        }

        [HttpPost("users/{id}/unlock")]
        public async Task<IActionResult> UnlockUser(long id)
        {
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            try
            {
                await conn.OpenAsync();

                string sql = $"UPDATE {DbSchema}.USERS SET LOCKOUT_END_DATE = NULL, FAILED_LOGIN_COUNT = 0 WHERE USER_ID = :p_id";

                using var cmd = new OracleCommand(sql, conn);
                cmd.Parameters.Add("p_id", id);
                await cmd.ExecuteNonQueryAsync();

                return Ok(new { message = "Unlocked successfully." });
            }
            catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }
        }

        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(long id)
        {
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            OracleTransaction? transaction = null;

            try
            {
                await conn.OpenAsync();
                transaction = conn.BeginTransaction();


                using (var cmd = new OracleCommand($"DELETE FROM {DbSchema}.USER_ROLES WHERE USER_ID = :p_id", conn))
                {
                    cmd.Transaction = transaction;
                    cmd.Parameters.Add("p_id", id);
                    await cmd.ExecuteNonQueryAsync();
                }


                using (var cmd = new OracleCommand($"DELETE FROM {DbSchema}.SCHEDULES WHERE USER_ID = :p_id", conn))
                {
                    cmd.Transaction = transaction;
                    cmd.Parameters.Add("p_id", id);
                    await cmd.ExecuteNonQueryAsync();
                }


                using (var cmd = new OracleCommand($"DELETE FROM {DbSchema}.USER_KEYS WHERE USER_ID = :p_id", conn))
                {
                    cmd.Transaction = transaction;
                    cmd.Parameters.Add("p_id", id);
                    await cmd.ExecuteNonQueryAsync();
                }


                using (var cmd = new OracleCommand($"DELETE FROM {DbSchema}.NOTIFICATIONS WHERE SENDER_ID = :p_id OR RECEIVER_ID = :p_id", conn))
                {
                    cmd.Transaction = transaction;
                    cmd.Parameters.Add("p_id", id);
                    cmd.Parameters.Add("p_id", id);
                    await cmd.ExecuteNonQueryAsync();
                }


                using (var cmd = new OracleCommand($"UPDATE {DbSchema}.AUDIT_LOGS SET USER_ID = NULL WHERE USER_ID = :p_id", conn))
                {
                    cmd.Transaction = transaction;
                    cmd.Parameters.Add("p_id", id);
                    await cmd.ExecuteNonQueryAsync();
                }


                using (var cmd = new OracleCommand($"DELETE FROM {DbSchema}.USERS WHERE USER_ID = :p_id", conn))
                {
                    cmd.Transaction = transaction;
                    cmd.Parameters.Add("p_id", id);
                    int rows = await cmd.ExecuteNonQueryAsync();

                    if (rows == 0)
                    {
                        transaction.Rollback();
                        return NotFound(new { message = "Không tìm thấy User." });
                    }
                }

                transaction.Commit();
                return Ok(new { message = "Đã xóa tài khoản và toàn bộ dữ liệu liên quan." });
            }
            catch (Exception ex)
            {
                transaction?.Rollback();

                return StatusCode(500, new { message = "Lỗi xóa: " + ex.Message });
            }
        }

        [HttpPost("users/{id}/promote")]
        public async Task<IActionResult> PromoteUser(long id)
        {
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            OracleTransaction? transaction = null;

            try
            {
                await conn.OpenAsync();
                transaction = conn.BeginTransaction();

                string getRoleSql = $"SELECT ROLE_ID FROM {DbSchema}.ROLES WHERE ROLE_NAME = 'NHAN_VIEN'";
                var roleCmd = new OracleCommand(getRoleSql, conn);
                roleCmd.Transaction = transaction;
                var roleIdObj = await roleCmd.ExecuteScalarAsync();

                if (roleIdObj == null)
                {
                    return BadRequest(new { message = "Role NHAN_VIEN not found." });
                }
                int nhanVienRoleId = Convert.ToInt32(roleIdObj);

                string deleteRoleSql = $"DELETE FROM {DbSchema}.USER_ROLES WHERE USER_ID = :p_uid";
                var delCmd = new OracleCommand(deleteRoleSql, conn);
                delCmd.Transaction = transaction;
                delCmd.Parameters.Add("p_uid", id);
                await delCmd.ExecuteNonQueryAsync();

                string insertRoleSql = $"INSERT INTO {DbSchema}.USER_ROLES (USER_ID, ROLE_ID) VALUES (:p_uid, :p_rid)";
                var insertCmd = new OracleCommand(insertRoleSql, conn);
                insertCmd.Transaction = transaction;
                insertCmd.Parameters.Add("p_uid", id);
                insertCmd.Parameters.Add("p_rid", nhanVienRoleId);
                await insertCmd.ExecuteNonQueryAsync();

                transaction.Commit();
                return Ok(new { message = "Promoted to NHAN_VIEN successfully." });
            }
            catch (Exception ex)
            {
                transaction?.Rollback();
                return StatusCode(500, new { message = "Error: " + ex.Message });
            }
        }


        [HttpGet("schedules")]
        public async Task<IActionResult> GetAllSchedules()
        {
            var list = new List<AdminScheduleDto>();
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));

            try
            {
                await conn.OpenAsync();




                string ctxSql = "BEGIN DBMS_SESSION.SET_IDENTIFIER('admin'); END;";
                using (var ctxCmd = new OracleCommand(ctxSql, conn))
                {
                    await ctxCmd.ExecuteNonQueryAsync();
                }


                string sql = $@"
            SELECT s.SCHEDULE_ID, u.USERNAME, s.TITLE, 
                   {DbSchema}.HYBRID_SCHEDULE_PKG.DECRYPT_SYM(s.DESCRIPTION) AS DECRYPTED_DESC, 
                   s.START_TIME, s.END_TIME
            FROM {DbSchema}.SCHEDULES s
            JOIN {DbSchema}.USERS u ON s.USER_ID = u.USER_ID
            ORDER BY s.SCHEDULE_ID DESC
            FETCH FIRST 50 ROWS ONLY";

                using var cmd = new OracleCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var start = reader.GetDateTime(4).ToString("dd/MM HH:mm");
                    var end = reader.GetDateTime(5).ToString("HH:mm");

                    list.Add(new AdminScheduleDto
                    {
                        Id = reader.GetInt64(0),
                        Username = reader.GetString(1),
                        Title = reader.GetString(2),

                        Description = reader.IsDBNull(3) ? "[Không thể giải mã]" : reader.GetString(3),
                        TimeRange = $"{start} - {end}"
                    });
                }
                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi lấy lịch trình: " + ex.Message });
            }
        }
    }
}