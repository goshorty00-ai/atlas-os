using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// UUID/GUID Generator - Generate various ID formats.
    /// </summary>
    public static class UuidGenerator
    {
        private static readonly Random _random = new();
        
        /// <summary>
        /// Handle UUID/ID generation commands
        /// </summary>
        public static Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // UUID/GUID
            if (lower.Contains("uuid") || lower.Contains("guid"))
            {
                var count = 1;
                var match = System.Text.RegularExpressions.Regex.Match(lower, @"(\d+)\s*(?:uuid|guid)");
                if (match.Success)
                    count = Math.Min(int.Parse(match.Groups[1].Value), 20);
                
                return Task.FromResult<string?>(GenerateUuids(count));
            }
            
            // Short ID
            if (lower.Contains("short id") || lower.Contains("shortid") || lower.Contains("nanoid"))
            {
                var length = 12;
                var match = System.Text.RegularExpressions.Regex.Match(lower, @"(\d+)\s*(?:char|length)");
                if (match.Success)
                    length = Math.Clamp(int.Parse(match.Groups[1].Value), 6, 32);
                
                return Task.FromResult<string?>(GenerateShortId(length));
            }
            
            // Snowflake ID (Twitter-style)
            if (lower.Contains("snowflake"))
            {
                return Task.FromResult<string?>(GenerateSnowflakeId());
            }
            
            // Random hex
            if (lower.Contains("random hex") || lower.Contains("hex id"))
            {
                var length = 16;
                var match = System.Text.RegularExpressions.Regex.Match(lower, @"(\d+)\s*(?:char|byte|length)");
                if (match.Success)
                    length = Math.Clamp(int.Parse(match.Groups[1].Value), 4, 64);
                
                return Task.FromResult<string?>(GenerateHexId(length));
            }
            
            // Object ID (MongoDB style)
            if (lower.Contains("objectid") || lower.Contains("object id") || lower.Contains("mongo"))
            {
                return Task.FromResult<string?>(GenerateObjectId());
            }
            
            // ULID
            if (lower.Contains("ulid"))
            {
                return Task.FromResult<string?>(GenerateUlid());
            }
            
            return Task.FromResult<string?>(null);
        }
        
        private static string GenerateUuids(int count)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🆔 **Generated UUID{(count > 1 ? "s" : "")}:**\n");
            
            var uuids = new string[count];
            for (int i = 0; i < count; i++)
            {
                uuids[i] = Guid.NewGuid().ToString();
                sb.AppendLine($"`{uuids[i]}`");
            }
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(count == 1 ? uuids[0] : string.Join("\n", uuids)));
            
            sb.AppendLine("\n✓ Copied to clipboard!");
            return sb.ToString();
        }
        
        private static string GenerateShortId(int length)
        {
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            var id = new char[length];
            var bytes = new byte[length];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            
            for (int i = 0; i < length; i++)
                id[i] = chars[bytes[i] % chars.Length];
            
            var result = new string(id);
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(result));
            
            return $"🆔 **Short ID ({length} chars):**\n```\n{result}\n```\n✓ Copied to clipboard!";
        }
        
        private static string GenerateSnowflakeId()
        {
            // Simplified snowflake: timestamp + random
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var random = _random.Next(0, 4096);
            var snowflake = (timestamp << 22) | (uint)random;
            
            var result = snowflake.ToString();
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(result));
            
            return $"❄️ **Snowflake ID:**\n```\n{result}\n```\n" +
                   $"Timestamp: {DateTimeOffset.FromUnixTimeMilliseconds(timestamp):yyyy-MM-dd HH:mm:ss}\n" +
                   $"✓ Copied to clipboard!";
        }
        
        private static string GenerateHexId(int length)
        {
            var bytes = new byte[(length + 1) / 2];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            var hex = BitConverter.ToString(bytes).Replace("-", "").ToLower();
            if (hex.Length > length) hex = hex.Substring(0, length);
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(hex));
            
            return $"🔢 **Random Hex ({length} chars):**\n```\n{hex}\n```\n✓ Copied to clipboard!";
        }
        
        private static string GenerateObjectId()
        {
            // MongoDB ObjectId format: 4-byte timestamp + 5-byte random + 3-byte counter
            var timestamp = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var random = new byte[5];
            System.Security.Cryptography.RandomNumberGenerator.Fill(random);
            var counter = _random.Next(0, 0xFFFFFF);
            
            var bytes = new byte[12];
            bytes[0] = (byte)(timestamp >> 24);
            bytes[1] = (byte)(timestamp >> 16);
            bytes[2] = (byte)(timestamp >> 8);
            bytes[3] = (byte)timestamp;
            Array.Copy(random, 0, bytes, 4, 5);
            bytes[9] = (byte)(counter >> 16);
            bytes[10] = (byte)(counter >> 8);
            bytes[11] = (byte)counter;
            
            var objectId = BitConverter.ToString(bytes).Replace("-", "").ToLower();
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(objectId));
            
            return $"🍃 **MongoDB ObjectId:**\n```\n{objectId}\n```\n✓ Copied to clipboard!";
        }
        
        private static string GenerateUlid()
        {
            // ULID: Crockford Base32 encoded timestamp + random
            const string encoding = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var random = new byte[10];
            System.Security.Cryptography.RandomNumberGenerator.Fill(random);
            
            var ulid = new char[26];
            
            // Encode timestamp (10 chars)
            ulid[0] = encoding[(int)((timestamp >> 45) & 31)];
            ulid[1] = encoding[(int)((timestamp >> 40) & 31)];
            ulid[2] = encoding[(int)((timestamp >> 35) & 31)];
            ulid[3] = encoding[(int)((timestamp >> 30) & 31)];
            ulid[4] = encoding[(int)((timestamp >> 25) & 31)];
            ulid[5] = encoding[(int)((timestamp >> 20) & 31)];
            ulid[6] = encoding[(int)((timestamp >> 15) & 31)];
            ulid[7] = encoding[(int)((timestamp >> 10) & 31)];
            ulid[8] = encoding[(int)((timestamp >> 5) & 31)];
            ulid[9] = encoding[(int)(timestamp & 31)];
            
            // Encode random (16 chars)
            for (int i = 0; i < 16; i++)
            {
                ulid[10 + i] = encoding[random[i % 10] & 31];
            }
            
            var result = new string(ulid);
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(result));
            
            return $"🆔 **ULID:**\n```\n{result}\n```\n" +
                   $"Sortable, URL-safe, 128-bit\n" +
                   $"✓ Copied to clipboard!";
        }
    }
}
