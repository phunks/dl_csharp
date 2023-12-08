using System.Data.SQLite;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using static System.Security.Cryptography.ProtectedData;

namespace ConsoleApp1
{
    public static class CookieJar
    {
        public static CookieCollection GetCookies()
        {
            var cookies = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                                         + Constants.Param.Cookies;
            if (!System.IO.File.Exists(cookies)) throw new System.IO
                    .FileNotFoundException("Cant find cookie store", cookies);
            var connectionString = "Data Source=" + cookies + ";pooling=false";
            var list = new List<Dictionary<string, string>>();
            var cookieCollection = new CookieCollection();
            using var conn = new SQLiteConnection(connectionString);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT host_key, is_httponly, path, is_secure, expires_utc, name, "
                                  + "decrypt(encrypted_value) as value FROM cookies where host_key like '%'";
                conn.Open();
                conn.BindFunction(new ModeGCM());
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var hostKey = (string)reader["host_key"];
                    var path = (string)reader["path"];
                    var name = (string)reader["name"];
                    var value = (string)reader["value"];
                    var issecure = ToBoolean(reader["is_secure"].ToString());
                    var expiresutc = EpochFromWebkit((long)reader["expires_utc"]);

                    list.Add(new Dictionary<string, string>() {
                                { "hostKey", hostKey },
                                { "ishttponly", "TRUE" },
                                { "path", path },
                                { "issecure", issecure },
                                { "expiresutc", expiresutc },
                                { "name", name },
                                { "value",value },
                            });
                    cookieCollection.Add(new System.Net.Cookie(name, HttpUtility.UrlEncode(value), path, hostKey));
                }
            }
            conn.Close();
            return cookieCollection;
        }
        private static string EpochFromWebkit(long webkitTimestamp)
        {
            long epochTime = 0;
            const long chromeEpochStart = 11644473600; // 1601.01.01
            webkitTimestamp /= 1000000;
            if (webkitTimestamp > chromeEpochStart) {
                epochTime = Convert.ToInt64(webkitTimestamp - chromeEpochStart);
            }
            return epochTime.ToString();
        }
        private static string ToBoolean(string? flag)
        {
            return flag == "0" ? "FALSE" : "TRUE";
        }
        
        public static CookieCollection MergeCookies(CookieCollection cur, CookieCollection @new) {
            foreach (System.Net.Cookie cookie in @new) {
                try {
                    cur.Remove(new System.Net.Cookie(cookie.Name, cur[cookie.Name].Value, cur[cookie.Name].Path,
                        cur[cookie.Name].Domain));
                }
                catch {
                }
                cur.Add(cookie);
            }
            return cur;
        }
    }
    
    public static class ExtensionMethods
    {
        public static void BindFunction(this SQLiteConnection connection, SQLiteFunction function)
        {
            var attributes = function.GetType().GetCustomAttributes(typeof(SQLiteFunctionAttribute),
                                                                true).Cast<SQLiteFunctionAttribute>().ToArray();
            if (attributes.Length == 0)
            {
                throw new InvalidOperationException("SQLiteFunction doesn't have SQLiteFunctionAttribute");
            }
            connection.BindFunction(attributes[0], function);
        }
    }

    [SQLiteFunction(Name = "decrypt", Arguments = 1, FuncType = FunctionType.Scalar)]
    public class ModeGCM : SQLiteFunction
    {
        private byte[]? key;
        const int nonSecretPayloadLength = 3;
        const int KEY_BIT_SIZE = 256;
        const int MAC_BIT_SIZE = 128;
        const int NONCE_BIT_SIZE = 96;
        public override object Invoke(object[] args)
        {
            var message = (byte[])args[0];

            key ??= GetKey();

            if (key is not { Length: KEY_BIT_SIZE / 8 })
                throw new ArgumentException($"Key needs to be {KEY_BIT_SIZE} bit!", "key");
            if (message == null || message.Length == 0)
                throw new ArgumentException("Message required!", "message");

            using var cipherStream = new MemoryStream(message);
            using var cipherReader = new BinaryReader(cipherStream);
            var nonSecretPayload = cipherReader.ReadBytes(nonSecretPayloadLength);
            var nonce = cipherReader.ReadBytes(NONCE_BIT_SIZE / 8);
            var cipher = new GcmBlockCipher(new AesEngine());
            var parameters = new AeadParameters(new KeyParameter(key), MAC_BIT_SIZE, nonce);
            cipher.Init(false, parameters);
            var cipherText = cipherReader.ReadBytes(message.Length);
            var plainText = new byte[cipher.GetOutputSize(cipherText.Length)];
            try
            {
                var len = cipher.ProcessBytes(cipherText, 0, cipherText.Length, plainText, 0);
                cipher.DoFinal(plainText, len);
            }
            catch (InvalidCipherTextException)
            {
                return null;
            }
            return Encoding.Default.GetString(plainText);
        }

        private static byte[] GetKey()
        {
            var encKey = File.ReadAllText(Environment.GetFolderPath(Environment
                                            .SpecialFolder.LocalApplicationData) + Constants.Param.LocalState);
            encKey = JObject.Parse(encKey)["os_crypt"]?["encrypted_key"]?.ToString();
            var key = Unprotect(Convert.FromBase64String(encKey)
                                .Skip(5).ToArray(), null, DataProtectionScope.LocalMachine);
            return key;
        }
    }
}

