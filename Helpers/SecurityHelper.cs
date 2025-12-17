using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
namespace ScheduleAppApi.Helpers
{
    public static class SecurityHelper
    {
        private static readonly string AppMasterKey = "E546C8DF278CD5931069B522E695D4F2";

        public static string Encrypt(string plainText) => Encrypt(plainText, Encoding.UTF8.GetBytes(AppMasterKey));
        public static string Decrypt(string cipherText) => Decrypt(cipherText, Encoding.UTF8.GetBytes(AppMasterKey));

        public static byte[] GenerateRandomKey()
        {
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.GenerateKey();
                return aes.Key;
            }
        }

        public static string Encrypt(string plainText, byte[] key)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = new byte[16];

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs))
                    {
                        sw.Write(plainText);
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        public static string Decrypt(string cipherText, byte[] key)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = new byte[16];

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    using (var ms = new MemoryStream(Convert.FromBase64String(cipherText)))
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    using (var sr = new StreamReader(cs))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
            catch
            {
                return "Lỗi giải mã: Sai khóa hoặc dữ liệu hỏng.";
            }
        }
        public static string HashPassword(string password)
        {
           
            return BCrypt.Net.BCrypt.HashPassword(password);
        }
    }
}