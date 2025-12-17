using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using ScheduleAppApi.Helpers;
using System.Data;
using System.Security.Claims;

namespace ScheduleAppApi.Controllers
{
    public record SendMessageDto(string ReceiverUsername, string MessageContent);
    public record NotificationDto(string Sender, string Content, string SentAt);

    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly IConfiguration _config;
        public NotificationsController(IConfiguration config) { _config = config; }

        private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        [HttpPost("send")]
        public IActionResult SendMessage([FromBody] SendMessageDto dto)
        {
            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            conn.Open();

            string receiverPubKeyRSA = "";
            int receiverId = 0;

            using (var cmd = new OracleCommand(
                "SELECT u.USER_ID, k.PUBLIC_KEY FROM USERS u JOIN USER_KEYS k ON u.USER_ID = k.USER_ID WHERE u.USERNAME = :p_username", conn))
            {
                cmd.BindByName = true;
                cmd.Parameters.Add("p_username", dto.ReceiverUsername);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    receiverId = reader.GetInt32(0);
                    receiverPubKeyRSA = reader.GetString(1);
                }
                else return NotFound(new { message = "Người nhận chưa có khóa bảo mật." });
            }

            byte[] sessionKey = SecurityHelper.GenerateRandomKey();

            string encryptedMessage = SecurityHelper.Encrypt(dto.MessageContent, sessionKey);

            string sessionKeyString = Convert.ToBase64String(sessionKey);
            string encryptedSessionKey = RsaHelper.Encrypt(sessionKeyString, receiverPubKeyRSA);

            string sqlInsert = @"
                INSERT INTO NOTIFICATIONS (SENDER_ID, RECEIVER_ID, ENCRYPTED_MESSAGE, ENCRYPTED_SESSION_KEY) 
                VALUES (:p_sid, :p_rid, :p_msg, :p_sess_key)";

            using (var cmd = new OracleCommand(sqlInsert, conn))
            {
                cmd.BindByName = true;
                cmd.Parameters.Add("p_sid", GetUserId());
                cmd.Parameters.Add("p_rid", receiverId);
                cmd.Parameters.Add("p_msg", OracleDbType.Clob).Value = encryptedMessage;
                cmd.Parameters.Add("p_sess_key", OracleDbType.Clob).Value = encryptedSessionKey;
                cmd.ExecuteNonQuery();
            }

            return Ok(new { message = "Đã gửi tin nhắn mật (Hybrid Encryption) thành công!" });
        }

        [HttpGet("inbox")]
        public IActionResult GetMyMessages()
        {
            var userId = GetUserId();
            var result = new List<NotificationDto>();

            using var conn = new OracleConnection(_config.GetConnectionString("OracleDb"));
            conn.Open();

            string myEncryptedPrivKey = "";
            using (var cmd = new OracleCommand("SELECT ENCRYPTED_PRIVATE_KEY FROM USER_KEYS WHERE USER_ID = :p_uid", conn))
            {
                cmd.BindByName = true;
                cmd.Parameters.Add("p_uid", userId);
                var obj = cmd.ExecuteScalar();
                if (obj == null) return BadRequest(new { message = "Bạn chưa có Key." });
                myEncryptedPrivKey = obj.ToString();
            }

            string myPrivateKeyRSA = SecurityHelper.Decrypt(myEncryptedPrivKey);

            string sqlGetMsg = @"
                SELECT u.USERNAME, n.ENCRYPTED_MESSAGE, n.ENCRYPTED_SESSION_KEY, n.CREATED_AT 
                FROM NOTIFICATIONS n
                JOIN USERS u ON n.SENDER_ID = u.USER_ID
                WHERE n.RECEIVER_ID = :p_rid
                ORDER BY n.CREATED_AT DESC";

            using (var cmd = new OracleCommand(sqlGetMsg, conn))
            {
                cmd.BindByName = true;
                cmd.Parameters.Add("p_rid", userId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string sender = reader.GetString(0);
                    string encryptedMsg = reader.GetString(1);
                    string encryptedSessionKey = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    var time = reader.GetDateTime(3);

                    string plainText = "Tin nhắn cũ (RSA thuần - không đọc được)";

                    if (!string.IsNullOrEmpty(encryptedSessionKey))
                    {
                        try
                        {
                            string sessionKeyString = RsaHelper.Decrypt(encryptedSessionKey, myPrivateKeyRSA);
                            byte[] sessionKey = Convert.FromBase64String(sessionKeyString);

                            plainText = SecurityHelper.Decrypt(encryptedMsg, sessionKey);
                        }
                        catch
                        {
                            plainText = "[Lỗi giải mã Hybrid]";
                        }
                    }
                    else
                    {
                        try { plainText = RsaHelper.Decrypt(encryptedMsg, myPrivateKeyRSA); } catch { }
                    }

                    result.Add(new NotificationDto(sender, plainText, time.ToString("dd/MM/yyyy HH:mm")));
                }
            }

            return Ok(result);
        }
    }
}