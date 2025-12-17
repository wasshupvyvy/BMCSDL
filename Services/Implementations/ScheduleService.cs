using ScheduleAppApi.Models;
using ScheduleAppApi.Services;
using Oracle.ManagedDataAccess.Client;
using System.Data;

namespace ScheduleAppApi.Services
{
    public class ScheduleService : IScheduleService
    {
        private readonly IConfiguration _config;

        public ScheduleService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<int> CreateScheduleAsync(CreateScheduleDto dto, int userId)
        {
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            await conn.OpenAsync();

            using var cmd = new OracleCommand("APPUSER.SECURE_INSERT_SCHEDULE", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.BindByName = true;

            cmd.Parameters.Add("p_user_id", OracleDbType.Int32).Value = userId;
            cmd.Parameters.Add("p_title", OracleDbType.Varchar2).Value = dto.Title;
            cmd.Parameters.Add("p_description", OracleDbType.Varchar2).Value = dto.Description;
            cmd.Parameters.Add("p_start_time", OracleDbType.TimeStamp).Value = dto.StartTime.DateTime;
            cmd.Parameters.Add("p_end_time", OracleDbType.TimeStamp).Value = dto.EndTime.DateTime;
            cmd.Parameters.Add("p_label_id", OracleDbType.Int32).Value = dto.SecurityLabelId > 0 ? dto.SecurityLabelId : 1;

            try
            {
                await cmd.ExecuteNonQueryAsync();
                return 1;
            }
            catch (OracleException ex)
            {
                if (ex.Number == 20001)
                {
                    throw new UnauthorizedAccessException("Bạn không có quyền tạo lịch.");
                }
                throw;
            }
        }

        public async Task<IEnumerable<ScheduleDto>> GetMySchedulesAsync(int userId)
        {
            var result = new List<ScheduleDto>();

            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            await conn.OpenAsync();

            string sql = @"
                SELECT 
                    SCHEDULE_ID, 
                    TITLE, 
                    APPUSER.HYBRID_SCHEDULE_PKG.DECRYPT_SYM(DESCRIPTION), 
                    CAST(START_TIME AS DATE), 
                    CAST(END_TIME AS DATE), 
                    SECURITY_LABEL_ID
                FROM APPUSER.SCHEDULES 
                WHERE USER_ID = :p_user_id
                ORDER BY START_TIME DESC";

            using var cmd = new OracleCommand(sql, conn);
            cmd.BindByName = true;
            cmd.Parameters.Add("p_user_id", userId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long sId = 0;
                if (!reader.IsDBNull(0)) sId = Convert.ToInt64(reader.GetValue(0));

                string title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                string desc = reader.IsDBNull(2) ? "" : reader.GetString(2);

                DateTime start = DateTime.Now;
                if (!reader.IsDBNull(3)) start = reader.GetDateTime(3);

                DateTime end = DateTime.Now;
                if (!reader.IsDBNull(4)) end = reader.GetDateTime(4);

                int labelId = 1;
                if (!reader.IsDBNull(5)) labelId = Convert.ToInt32(reader.GetValue(5));

                result.Add(new ScheduleDto(
                    sId,
                    title,
                    desc,
                    new DateTimeOffset(start),
                    new DateTimeOffset(end),
                    labelId
                ));
            }

            return result;
        }

        public async Task<bool> UpdateScheduleAsync(int scheduleId, UpdateScheduleDto dto, int userId)
        {
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            await conn.OpenAsync();

            string sql = @"
                UPDATE APPUSER.SCHEDULES 
                SET TITLE = :title, 
                    DESCRIPTION = APPUSER.HYBRID_SCHEDULE_PKG.ENCRYPT_SYM(:description),
                    START_TIME = :startTime,
                    END_TIME = :endTime
                WHERE SCHEDULE_ID = :id AND USER_ID = :userId";

            using var cmd = new OracleCommand(sql, conn);
            cmd.BindByName = true;

            cmd.Parameters.Add("title", dto.Title);
            cmd.Parameters.Add("description", dto.Description);
            cmd.Parameters.Add("startTime", dto.StartTime.DateTime);
            cmd.Parameters.Add("endTime", dto.EndTime.DateTime);
            cmd.Parameters.Add("id", scheduleId);
            cmd.Parameters.Add("userId", userId);

            int rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<bool> DeleteScheduleAsync(int scheduleId, int userId)
        {
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            await conn.OpenAsync();

            string sql = "DELETE FROM APPUSER.SCHEDULES WHERE SCHEDULE_ID = :id AND USER_ID = :userId";

            using var cmd = new OracleCommand(sql, conn);
            cmd.BindByName = true;
            cmd.Parameters.Add("id", scheduleId);
            cmd.Parameters.Add("userId", userId);

            int rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public Task<IEnumerable<ScheduleDto>> GetAllSchedulesAsync()
        {
            throw new NotImplementedException();
        }
    }
}