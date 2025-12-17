using System.Security.Cryptography;
using System.Text;

namespace ScheduleAppApi.Helpers
{
    public static class RsaHelper
    {
        public static (string PublicKey, string PrivateKey) GenerateKeys()
        {
            using (var rsa = RSA.Create(2048))
            {
                string publicKey = rsa.ExportSubjectPublicKeyInfoPem();
                string privateKey = rsa.ExportPkcs8PrivateKeyPem();
                return (publicKey, privateKey);
            }
        }

        public static string Encrypt(string plainText, string publicKeyPem)
        {
            using (var rsa = RSA.Create())
            {
                rsa.ImportFromPem(publicKeyPem);
                byte[] data = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = rsa.Encrypt(data, RSAEncryptionPadding.Pkcs1);
                return Convert.ToBase64String(encrypted);
            }
        }

        public static string Decrypt(string encryptedBase64, string privateKeyPem)
        {
            using (var rsa = RSA.Create())
            {
                rsa.ImportFromPem(privateKeyPem);
                byte[] encryptedData = Convert.FromBase64String(encryptedBase64);
                byte[] decrypted = rsa.Decrypt(encryptedData, RSAEncryptionPadding.Pkcs1);
                return Encoding.UTF8.GetString(decrypted);
            }
        }
    }
}