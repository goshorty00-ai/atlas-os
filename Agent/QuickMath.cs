using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Quick Math - Advanced math operations, statistics, number theory.
    /// </summary>
    public static class QuickMath
    {
        /// <summary>
        /// Handle advanced math commands
        /// </summary>
        public static Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Prime check
            if (lower.Contains("prime") && lower.Contains("check") || lower.StartsWith("is ") && lower.Contains("prime"))
            {
                var match = Regex.Match(input, @"(\d+)");
                if (match.Success)
                {
                    var num = long.Parse(match.Groups[1].Value);
                    return Task.FromResult<string?>(CheckPrime(num));
                }
            }
            
            // Prime factors
            if (lower.Contains("factor") || lower.Contains("factorize"))
            {
                var match = Regex.Match(input, @"(\d+)");
                if (match.Success)
                {
                    var num = long.Parse(match.Groups[1].Value);
                    return Task.FromResult<string?>(GetPrimeFactors(num));
                }
            }
            
            // GCD/LCM
            if (lower.Contains("gcd") || lower.Contains("greatest common"))
            {
                var matches = Regex.Matches(input, @"(\d+)");
                if (matches.Count >= 2)
                {
                    var nums = matches.Cast<Match>().Select(m => long.Parse(m.Value)).ToArray();
                    return Task.FromResult<string?>(CalculateGcd(nums));
                }
            }
            
            if (lower.Contains("lcm") || lower.Contains("least common"))
            {
                var matches = Regex.Matches(input, @"(\d+)");
                if (matches.Count >= 2)
                {
                    var nums = matches.Cast<Match>().Select(m => long.Parse(m.Value)).ToArray();
                    return Task.FromResult<string?>(CalculateLcm(nums));
                }
            }
            
            // Fibonacci
            if (lower.Contains("fibonacci") || lower.Contains("fib"))
            {
                var match = Regex.Match(input, @"(\d+)");
                if (match.Success)
                {
                    var n = int.Parse(match.Groups[1].Value);
                    return Task.FromResult<string?>(GetFibonacci(n));
                }
            }
            
            // Factorial
            if (lower.Contains("factorial"))
            {
                var match = Regex.Match(input, @"(\d+)");
                if (match.Success)
                {
                    var n = int.Parse(match.Groups[1].Value);
                    return Task.FromResult<string?>(CalculateFactorial(n));
                }
            }
            
            // Statistics (average, median, etc.)
            if (lower.Contains("average") || lower.Contains("mean") || lower.Contains("median") || 
                lower.Contains("stats") || lower.Contains("statistics"))
            {
                var matches = Regex.Matches(input, @"[\d.]+");
                if (matches.Count >= 2)
                {
                    var nums = matches.Cast<Match>().Select(m => double.Parse(m.Value)).ToArray();
                    return Task.FromResult<string?>(CalculateStatistics(nums));
                }
                
                // Try clipboard
                string? clipText = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText())
                        clipText = Clipboard.GetText();
                });
                
                if (!string.IsNullOrEmpty(clipText))
                {
                    var clipMatches = Regex.Matches(clipText, @"[\d.]+");
                    if (clipMatches.Count >= 2)
                    {
                        var nums = clipMatches.Cast<Match>().Select(m => double.Parse(m.Value)).ToArray();
                        return Task.FromResult<string?>(CalculateStatistics(nums));
                    }
                }
            }
            
            // Binary/Hex/Decimal conversion
            if (lower.Contains("binary") || lower.Contains("hex") || lower.Contains("decimal"))
            {
                return Task.FromResult<string?>(ConvertNumber(input, lower));
            }
            
            // Percentage
            if (lower.Contains("percent") || lower.Contains("%"))
            {
                return Task.FromResult<string?>(CalculatePercentage(input, lower));
            }
            
            return Task.FromResult<string?>(null);
        }
        
        private static string CheckPrime(long n)
        {
            if (n < 2) return $"❌ {n} is not prime (must be ≥ 2)";
            if (n == 2) return $"✅ **2 is prime!** (the only even prime)";
            if (n % 2 == 0) return $"❌ **{n} is not prime** (divisible by 2)";
            
            for (long i = 3; i * i <= n; i += 2)
            {
                if (n % i == 0)
                    return $"❌ **{n} is not prime**\n\nDivisible by {i} ({n} = {i} × {n / i})";
            }
            
            return $"✅ **{n:N0} is prime!**";
        }
        
        private static string GetPrimeFactors(long n)
        {
            if (n < 2) return "Number must be ≥ 2";
            
            var factors = new List<long>();
            var original = n;
            
            while (n % 2 == 0)
            {
                factors.Add(2);
                n /= 2;
            }
            
            for (long i = 3; i * i <= n; i += 2)
            {
                while (n % i == 0)
                {
                    factors.Add(i);
                    n /= i;
                }
            }
            
            if (n > 2) factors.Add(n);
            
            var grouped = factors.GroupBy(f => f)
                .Select(g => g.Count() > 1 ? $"{g.Key}^{g.Count()}" : g.Key.ToString());
            
            return $"🔢 **Prime Factorization of {original:N0}:**\n\n" +
                   $"{original:N0} = {string.Join(" × ", grouped)}";
        }
        
        private static string CalculateGcd(long[] nums)
        {
            long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);
            var result = nums.Aggregate(Gcd);
            
            return $"🔢 **GCD({string.Join(", ", nums)}) = {result}**";
        }
        
        private static string CalculateLcm(long[] nums)
        {
            long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);
            long Lcm(long a, long b) => a / Gcd(a, b) * b;
            var result = nums.Aggregate(Lcm);
            
            return $"🔢 **LCM({string.Join(", ", nums)}) = {result:N0}**";
        }
        
        private static string GetFibonacci(int n)
        {
            if (n < 1) return "Position must be ≥ 1";
            if (n > 93) return "Position too large (max 93 for 64-bit)";
            
            long a = 0, b = 1;
            for (int i = 2; i <= n; i++)
            {
                var temp = a + b;
                a = b;
                b = temp;
            }
            
            var result = n == 1 ? 0 : b;
            
            // Show sequence
            var seq = new List<long>();
            a = 0; b = 1;
            for (int i = 1; i <= Math.Min(n, 15); i++)
            {
                seq.Add(i == 1 ? 0 : (i == 2 ? 1 : (a + b)));
                if (i > 2) { var t = a + b; a = b; b = t; }
                else if (i == 2) { a = 0; b = 1; }
            }
            
            return $"🔢 **Fibonacci({n}) = {result:N0}**\n\n" +
                   $"Sequence: {string.Join(", ", seq)}{(n > 15 ? "..." : "")}";
        }
        
        private static string CalculateFactorial(int n)
        {
            if (n < 0) return "Factorial not defined for negative numbers";
            if (n > 20) return "Number too large (max 20 for 64-bit)";
            
            long result = 1;
            for (int i = 2; i <= n; i++)
                result *= i;
            
            return $"🔢 **{n}! = {result:N0}**";
        }
        
        private static string CalculateStatistics(double[] nums)
        {
            var sorted = nums.OrderBy(n => n).ToArray();
            var count = nums.Length;
            var sum = nums.Sum();
            var mean = sum / count;
            var median = count % 2 == 0 
                ? (sorted[count / 2 - 1] + sorted[count / 2]) / 2 
                : sorted[count / 2];
            var min = sorted[0];
            var max = sorted[count - 1];
            var range = max - min;
            var variance = nums.Sum(n => Math.Pow(n - mean, 2)) / count;
            var stdDev = Math.Sqrt(variance);
            
            // Mode
            var mode = nums.GroupBy(n => n)
                .OrderByDescending(g => g.Count())
                .First().Key;
            
            return $"📊 **Statistics ({count} numbers):**\n\n" +
                   $"**Sum:** {sum:N2}\n" +
                   $"**Mean:** {mean:N2}\n" +
                   $"**Median:** {median:N2}\n" +
                   $"**Mode:** {mode:N2}\n" +
                   $"**Min:** {min:N2}\n" +
                   $"**Max:** {max:N2}\n" +
                   $"**Range:** {range:N2}\n" +
                   $"**Std Dev:** {stdDev:N2}";
        }
        
        private static string ConvertNumber(string input, string lower)
        {
            var match = Regex.Match(input, @"(0x[0-9a-fA-F]+|0b[01]+|\d+)");
            if (!match.Success) return "Specify a number to convert";
            
            var numStr = match.Groups[1].Value;
            long value;
            
            if (numStr.StartsWith("0x"))
                value = Convert.ToInt64(numStr.Substring(2), 16);
            else if (numStr.StartsWith("0b"))
                value = Convert.ToInt64(numStr.Substring(2), 2);
            else
                value = long.Parse(numStr);
            
            var binary = Convert.ToString(value, 2);
            var hex = Convert.ToString(value, 16).ToUpper();
            var dec = value.ToString();
            var oct = Convert.ToString(value, 8);
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (lower.Contains("binary")) Clipboard.SetText(binary);
                else if (lower.Contains("hex")) Clipboard.SetText("0x" + hex);
                else Clipboard.SetText(dec);
            });
            
            return $"🔢 **Number Conversion:**\n\n" +
                   $"**Decimal:** {dec}\n" +
                   $"**Binary:** 0b{binary}\n" +
                   $"**Hex:** 0x{hex}\n" +
                   $"**Octal:** 0o{oct}\n\n" +
                   $"✓ Copied to clipboard!";
        }
        
        private static string CalculatePercentage(string input, string lower)
        {
            // "what is 20% of 150"
            var match = Regex.Match(input, @"(\d+(?:\.\d+)?)\s*%\s*of\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var percent = double.Parse(match.Groups[1].Value);
                var total = double.Parse(match.Groups[2].Value);
                var result = total * percent / 100;
                return $"🔢 **{percent}% of {total} = {result:N2}**";
            }
            
            // "50 is what percent of 200"
            match = Regex.Match(input, @"(\d+(?:\.\d+)?)\s*(?:is\s+)?(?:what\s+)?percent(?:age)?\s*of\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var part = double.Parse(match.Groups[1].Value);
                var total = double.Parse(match.Groups[2].Value);
                var percent = (part / total) * 100;
                return $"🔢 **{part} is {percent:N2}% of {total}**";
            }
            
            // "increase 100 by 20%"
            match = Regex.Match(input, @"(?:increase|add)\s*(\d+(?:\.\d+)?)\s*by\s*(\d+(?:\.\d+)?)\s*%", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var value = double.Parse(match.Groups[1].Value);
                var percent = double.Parse(match.Groups[2].Value);
                var result = value * (1 + percent / 100);
                return $"🔢 **{value} + {percent}% = {result:N2}**";
            }
            
            return null!;
        }
    }
}
