using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Dice Roller - Roll dice, flip coins, pick random items.
    /// </summary>
    public static class DiceRoller
    {
        /// <summary>
        /// Handle random/dice commands
        /// </summary>
        public static Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Roll dice (d20, 2d6, etc.)
            var diceMatch = Regex.Match(lower, @"(?:roll\s+)?(\d*)d(\d+)(?:\s*\+\s*(\d+))?");
            if (diceMatch.Success)
            {
                var count = string.IsNullOrEmpty(diceMatch.Groups[1].Value) ? 1 : int.Parse(diceMatch.Groups[1].Value);
                var sides = int.Parse(diceMatch.Groups[2].Value);
                var modifier = diceMatch.Groups[3].Success ? int.Parse(diceMatch.Groups[3].Value) : 0;
                return Task.FromResult<string?>(RollDice(count, sides, modifier));
            }
            
            // Simple roll
            if (lower.StartsWith("roll ") || lower == "roll")
            {
                var numMatch = Regex.Match(lower, @"roll\s+(\d+)");
                if (numMatch.Success)
                {
                    var max = int.Parse(numMatch.Groups[1].Value);
                    return Task.FromResult<string?>(RollNumber(1, max));
                }
                // Default d20
                return Task.FromResult<string?>(RollDice(1, 20, 0));
            }
            
            // Flip coin
            if (lower.Contains("flip") && lower.Contains("coin") || lower == "heads or tails" || lower == "coin flip")
            {
                var count = 1;
                var countMatch = Regex.Match(lower, @"(\d+)\s*coin");
                if (countMatch.Success)
                    count = Math.Min(int.Parse(countMatch.Groups[1].Value), 100);
                return Task.FromResult<string?>(FlipCoins(count));
            }
            
            // Random number
            if (lower.Contains("random") && lower.Contains("number"))
            {
                var rangeMatch = Regex.Match(lower, @"(\d+)\s*(?:to|-)\s*(\d+)");
                if (rangeMatch.Success)
                {
                    var min = int.Parse(rangeMatch.Groups[1].Value);
                    var max = int.Parse(rangeMatch.Groups[2].Value);
                    return Task.FromResult<string?>(RollNumber(min, max));
                }
                
                var maxMatch = Regex.Match(lower, @"(\d+)");
                if (maxMatch.Success)
                {
                    var max = int.Parse(maxMatch.Groups[1].Value);
                    return Task.FromResult<string?>(RollNumber(1, max));
                }
                
                return Task.FromResult<string?>(RollNumber(1, 100));
            }
            
            // Pick random item
            if (lower.StartsWith("pick ") || lower.StartsWith("choose ") || lower.StartsWith("random "))
            {
                var itemsMatch = Regex.Match(input, @"(?:pick|choose|random)\s+(?:from\s+)?(.+)", RegexOptions.IgnoreCase);
                if (itemsMatch.Success)
                {
                    var itemsStr = itemsMatch.Groups[1].Value;
                    // Split by comma, "or", or space
                    var items = Regex.Split(itemsStr, @"\s*(?:,|or)\s*")
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray();
                    
                    if (items.Length > 1)
                        return Task.FromResult<string?>(PickRandom(items));
                }
            }
            
            // 8-ball
            if (lower.Contains("8 ball") || lower.Contains("8ball") || lower.Contains("magic ball"))
            {
                return Task.FromResult<string?>(Magic8Ball());
            }
            
            // Shuffle
            if (lower.StartsWith("shuffle "))
            {
                var itemsStr = input.Substring(8);
                var items = Regex.Split(itemsStr, @"\s*,\s*")
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
                
                if (items.Length > 1)
                    return Task.FromResult<string?>(ShuffleItems(items));
            }
            
            return Task.FromResult<string?>(null);
        }
        
        private static string RollDice(int count, int sides, int modifier)
        {
            count = Math.Clamp(count, 1, 100);
            sides = Math.Clamp(sides, 2, 1000);
            
            var rolls = new int[count];
            for (int i = 0; i < count; i++)
                rolls[i] = RandomNumberGenerator.GetInt32(1, sides + 1);
            
            var total = rolls.Sum() + modifier;
            
            var sb = new StringBuilder();
            sb.AppendLine($"🎲 **Rolling {count}d{sides}{(modifier > 0 ? $"+{modifier}" : "")}:**\n");
            
            if (count <= 20)
            {
                sb.AppendLine($"Rolls: [{string.Join(", ", rolls)}]");
            }
            else
            {
                sb.AppendLine($"Rolls: [{string.Join(", ", rolls.Take(10))}... +{count - 10} more]");
            }
            
            if (modifier > 0)
                sb.AppendLine($"Modifier: +{modifier}");
            
            sb.AppendLine($"\n**Total: {total}**");
            
            // Special messages for d20
            if (count == 1 && sides == 20)
            {
                if (rolls[0] == 20) sb.AppendLine("\n🎉 **NATURAL 20! CRITICAL!**");
                else if (rolls[0] == 1) sb.AppendLine("\n💀 **NATURAL 1! CRITICAL FAIL!**");
            }
            
            return sb.ToString();
        }
        
        private static string RollNumber(int min, int max)
        {
            if (min > max) (min, max) = (max, min);
            var result = RandomNumberGenerator.GetInt32(min, max + 1);
            return $"🎲 **Random number ({min}-{max}):**\n\n**{result}**";
        }
        
        private static string FlipCoins(int count)
        {
            var results = new bool[count];
            for (int i = 0; i < count; i++)
                results[i] = RandomNumberGenerator.GetInt32(2) == 0;
            
            var heads = results.Count(r => r);
            var tails = count - heads;
            
            if (count == 1)
            {
                var result = results[0] ? "Heads" : "Tails";
                var emoji = results[0] ? "🪙" : "⚪";
                return $"{emoji} **Coin Flip:**\n\n**{result}!**";
            }
            
            var sb = new StringBuilder();
            sb.AppendLine($"🪙 **Flipping {count} coins:**\n");
            
            if (count <= 20)
            {
                sb.AppendLine($"Results: {string.Join(" ", results.Select(r => r ? "H" : "T"))}");
            }
            
            sb.AppendLine($"\n**Heads:** {heads} ({heads * 100.0 / count:F1}%)");
            sb.AppendLine($"**Tails:** {tails} ({tails * 100.0 / count:F1}%)");
            
            return sb.ToString();
        }
        
        private static string PickRandom(string[] items)
        {
            var index = RandomNumberGenerator.GetInt32(items.Length);
            var picked = items[index];
            
            return $"🎯 **Random Pick:**\n\n" +
                   $"From: {string.Join(", ", items)}\n\n" +
                   $"**Winner: {picked}**";
        }
        
        private static string ShuffleItems(string[] items)
        {
            var shuffled = items.OrderBy(_ => RandomNumberGenerator.GetInt32(1000)).ToArray();
            
            return $"🔀 **Shuffled:**\n\n" +
                   $"Original: {string.Join(", ", items)}\n\n" +
                   $"**Result: {string.Join(", ", shuffled)}**";
        }
        
        private static string Magic8Ball()
        {
            var responses = new[]
            {
                "It is certain", "It is decidedly so", "Without a doubt",
                "Yes definitely", "You may rely on it", "As I see it, yes",
                "Most likely", "Outlook good", "Yes", "Signs point to yes",
                "Reply hazy, try again", "Ask again later", "Better not tell you now",
                "Cannot predict now", "Concentrate and ask again",
                "Don't count on it", "My reply is no", "My sources say no",
                "Outlook not so good", "Very doubtful"
            };
            
            var response = responses[RandomNumberGenerator.GetInt32(responses.Length)];
            return $"🎱 **Magic 8-Ball says:**\n\n**\"{response}\"**";
        }
    }
}
