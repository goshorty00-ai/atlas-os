using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Quick Calculator - Natural language math and conversions.
    /// "What's 15% of 200?"
    /// "Convert 100 USD to GBP"
    /// "How many days until Christmas?"
    /// </summary>
    public static class QuickCalculator
    {
        // Exchange rates (approximate - would need API for real rates)
        private static readonly Dictionary<string, double> ExchangeRates = new()
        {
            { "usd", 1.0 },
            { "gbp", 0.79 },
            { "eur", 0.92 },
            { "cad", 1.36 },
            { "aud", 1.53 },
            { "jpy", 149.5 },
            { "inr", 83.1 },
            { "cny", 7.24 },
        };
        
        /// <summary>
        /// Try to calculate from natural language
        /// </summary>
        public static string? TryCalculate(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Percentage calculations
            var result = TryPercentage(lower);
            if (result != null) return result;
            
            // Currency conversion
            result = TryCurrencyConversion(lower);
            if (result != null) return result;
            
            // Date calculations
            result = TryDateCalculation(lower);
            if (result != null) return result;
            
            // Unit conversions
            result = TryUnitConversion(lower);
            if (result != null) return result;
            
            // Time calculations
            result = TryTimeCalculation(lower);
            if (result != null) return result;
            
            // Basic math expression
            result = TryMathExpression(input);
            if (result != null) return result;
            
            return null;
        }
        
        private static string? TryPercentage(string lower)
        {
            // "X% of Y"
            var match = Regex.Match(lower, @"(\d+(?:\.\d+)?)\s*%\s*of\s*(\d+(?:\.\d+)?)");
            if (match.Success)
            {
                var percent = double.Parse(match.Groups[1].Value);
                var value = double.Parse(match.Groups[2].Value);
                var result = (percent / 100) * value;
                return $"🔢 {percent}% of {value} = **{result:N2}**";
            }
            
            // "what percent is X of Y"
            match = Regex.Match(lower, @"what\s+percent(?:age)?\s+is\s+(\d+(?:\.\d+)?)\s+of\s+(\d+(?:\.\d+)?)");
            if (match.Success)
            {
                var part = double.Parse(match.Groups[1].Value);
                var whole = double.Parse(match.Groups[2].Value);
                var result = (part / whole) * 100;
                return $"🔢 {part} is **{result:N2}%** of {whole}";
            }
            
            // "X plus/minus Y%"
            match = Regex.Match(lower, @"(\d+(?:\.\d+)?)\s*(plus|minus|\+|\-)\s*(\d+(?:\.\d+)?)\s*%");
            if (match.Success)
            {
                var value = double.Parse(match.Groups[1].Value);
                var op = match.Groups[2].Value;
                var percent = double.Parse(match.Groups[3].Value);
                var change = (percent / 100) * value;
                var result = (op == "plus" || op == "+") ? value + change : value - change;
                return $"🔢 {value} {op} {percent}% = **{result:N2}**";
            }
            
            // Tip calculator "tip on X" or "20% tip on X"
            match = Regex.Match(lower, @"(?:(\d+)\s*%\s+)?tip\s+(?:on\s+)?(?:\$)?(\d+(?:\.\d+)?)");
            if (match.Success)
            {
                var tipPercent = match.Groups[1].Success ? double.Parse(match.Groups[1].Value) : 20;
                var bill = double.Parse(match.Groups[2].Value);
                var tip = (tipPercent / 100) * bill;
                var total = bill + tip;
                return $"💰 Bill: ${bill:N2}\nTip ({tipPercent}%): ${tip:N2}\n**Total: ${total:N2}**";
            }
            
            return null;
        }
        
        private static string? TryCurrencyConversion(string lower)
        {
            // "X USD to GBP" or "convert X dollars to pounds"
            var match = Regex.Match(lower, @"(?:convert\s+)?(\d+(?:\.\d+)?)\s*(usd|gbp|eur|cad|aud|jpy|inr|cny|dollars?|pounds?|euros?|yen)\s+(?:to|in)\s+(usd|gbp|eur|cad|aud|jpy|inr|cny|dollars?|pounds?|euros?|yen)");
            if (match.Success)
            {
                var amount = double.Parse(match.Groups[1].Value);
                var from = NormalizeCurrency(match.Groups[2].Value);
                var to = NormalizeCurrency(match.Groups[3].Value);
                
                if (ExchangeRates.TryGetValue(from, out var fromRate) && 
                    ExchangeRates.TryGetValue(to, out var toRate))
                {
                    var inUsd = amount / fromRate;
                    var result = inUsd * toRate;
                    return $"💱 {amount:N2} {from.ToUpper()} = **{result:N2} {to.ToUpper()}**\n_(approximate rate)_";
                }
            }
            
            return null;
        }
        
        private static string NormalizeCurrency(string currency)
        {
            return currency.ToLower() switch
            {
                "dollar" or "dollars" => "usd",
                "pound" or "pounds" => "gbp",
                "euro" or "euros" => "eur",
                _ => currency.ToLower()
            };
        }
        
        private static string? TryDateCalculation(string lower)
        {
            // "days until X"
            var match = Regex.Match(lower, @"(?:how\s+many\s+)?days?\s+(?:until|till|to)\s+(.+)");
            if (match.Success)
            {
                var dateStr = match.Groups[1].Value.Trim();
                var targetDate = ParseFuzzyDate(dateStr);
                if (targetDate.HasValue)
                {
                    var days = (targetDate.Value.Date - DateTime.Today).Days;
                    if (days < 0)
                        return $"📅 {dateStr} was {Math.Abs(days)} days ago";
                    if (days == 0)
                        return $"📅 {dateStr} is **today**!";
                    if (days == 1)
                        return $"📅 {dateStr} is **tomorrow**!";
                    return $"📅 **{days} days** until {dateStr}";
                }
            }
            
            // "what day is X"
            match = Regex.Match(lower, @"what\s+day\s+(?:is|was)\s+(.+)");
            if (match.Success)
            {
                var dateStr = match.Groups[1].Value.Trim();
                var date = ParseFuzzyDate(dateStr);
                if (date.HasValue)
                    return $"📅 {dateStr} is a **{date.Value:dddd}** ({date.Value:MMMM d, yyyy})";
            }
            
            // "X days from now"
            match = Regex.Match(lower, @"(\d+)\s+days?\s+from\s+(?:now|today)");
            if (match.Success)
            {
                var days = int.Parse(match.Groups[1].Value);
                var date = DateTime.Today.AddDays(days);
                return $"📅 {days} days from now is **{date:dddd, MMMM d, yyyy}**";
            }
            
            // "X weeks from now"
            match = Regex.Match(lower, @"(\d+)\s+weeks?\s+from\s+(?:now|today)");
            if (match.Success)
            {
                var weeks = int.Parse(match.Groups[1].Value);
                var date = DateTime.Today.AddDays(weeks * 7);
                return $"📅 {weeks} weeks from now is **{date:dddd, MMMM d, yyyy}**";
            }
            
            return null;
        }
        
        private static DateTime? ParseFuzzyDate(string input)
        {
            var lower = input.ToLower().Trim();
            var now = DateTime.Now;
            
            // Special dates
            if (lower == "christmas" || lower == "xmas")
                return new DateTime(now.Month > 12 || (now.Month == 12 && now.Day > 25) ? now.Year + 1 : now.Year, 12, 25);
            if (lower == "new year" || lower == "new years" || lower == "new year's")
                return new DateTime(now.Year + 1, 1, 1);
            if (lower == "halloween")
                return new DateTime(now.Month > 10 || (now.Month == 10 && now.Day > 31) ? now.Year + 1 : now.Year, 10, 31);
            if (lower == "valentine's" || lower == "valentines" || lower == "valentine's day")
                return new DateTime(now.Month > 2 || (now.Month == 2 && now.Day > 14) ? now.Year + 1 : now.Year, 2, 14);
            
            // Day names
            var dayNames = new[] { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };
            var dayIndex = Array.FindIndex(dayNames, d => lower.StartsWith(d));
            if (dayIndex >= 0)
            {
                var daysUntil = ((dayIndex - (int)now.DayOfWeek) + 7) % 7;
                if (daysUntil == 0) daysUntil = 7;
                return now.AddDays(daysUntil).Date;
            }
            
            // Try standard date parsing
            if (DateTime.TryParse(input, out var parsed))
                return parsed;
            
            return null;
        }
        
        private static string? TryUnitConversion(string lower)
        {
            // Temperature
            var match = Regex.Match(lower, @"(\d+(?:\.\d+)?)\s*°?\s*(c|celsius|f|fahrenheit)\s+(?:to|in)\s+(c|celsius|f|fahrenheit)");
            if (match.Success)
            {
                var value = double.Parse(match.Groups[1].Value);
                var from = match.Groups[2].Value[0];
                var to = match.Groups[3].Value[0];
                
                double result;
                if (from == 'c' && to == 'f')
                    result = (value * 9 / 5) + 32;
                else if (from == 'f' && to == 'c')
                    result = (value - 32) * 5 / 9;
                else
                    result = value;
                
                return $"🌡️ {value}°{char.ToUpper(from)} = **{result:N1}°{char.ToUpper(to)}**";
            }
            
            // Distance
            match = Regex.Match(lower, @"(\d+(?:\.\d+)?)\s*(km|kilometers?|mi|miles?)\s+(?:to|in)\s+(km|kilometers?|mi|miles?)");
            if (match.Success)
            {
                var value = double.Parse(match.Groups[1].Value);
                var from = match.Groups[2].Value.StartsWith("k") ? "km" : "mi";
                var to = match.Groups[3].Value.StartsWith("k") ? "km" : "mi";
                
                var result = from == "km" && to == "mi" ? value * 0.621371 :
                             from == "mi" && to == "km" ? value * 1.60934 : value;
                
                return $"📏 {value} {from} = **{result:N2} {to}**";
            }
            
            // Weight
            match = Regex.Match(lower, @"(\d+(?:\.\d+)?)\s*(kg|kilograms?|lbs?|pounds?)\s+(?:to|in)\s+(kg|kilograms?|lbs?|pounds?)");
            if (match.Success)
            {
                var value = double.Parse(match.Groups[1].Value);
                var from = match.Groups[2].Value.StartsWith("k") ? "kg" : "lb";
                var to = match.Groups[3].Value.StartsWith("k") ? "kg" : "lb";
                
                var result = from == "kg" && to == "lb" ? value * 2.20462 :
                             from == "lb" && to == "kg" ? value * 0.453592 : value;
                
                return $"⚖️ {value} {from} = **{result:N2} {to}**";
            }
            
            return null;
        }
        
        private static string? TryTimeCalculation(string lower)
        {
            // "X hours and Y minutes in minutes"
            var match = Regex.Match(lower, @"(\d+)\s*(?:hours?|hr?)\s*(?:and\s*)?(\d+)?\s*(?:minutes?|min)?\s+in\s+(minutes?|hours?|seconds?)");
            if (match.Success)
            {
                var hours = int.Parse(match.Groups[1].Value);
                var minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
                var totalMinutes = hours * 60 + minutes;
                var to = match.Groups[3].Value;
                
                if (to.StartsWith("min"))
                    return $"⏱️ {hours}h {minutes}m = **{totalMinutes} minutes**";
                if (to.StartsWith("sec"))
                    return $"⏱️ {hours}h {minutes}m = **{totalMinutes * 60} seconds**";
            }
            
            // Time zone conversion (simplified)
            match = Regex.Match(lower, @"what\s+time\s+(?:is\s+it\s+)?in\s+(.+)");
            if (match.Success)
            {
                var location = match.Groups[1].Value.Trim();
                var offset = GetTimezoneOffset(location);
                if (offset.HasValue)
                {
                    var time = DateTime.UtcNow.AddHours(offset.Value);
                    return $"🕐 Time in {location}: **{time:h:mm tt}** ({time:dddd})";
                }
            }
            
            return null;
        }
        
        private static double? GetTimezoneOffset(string location)
        {
            var lower = location.ToLower();
            return lower switch
            {
                "london" or "uk" or "england" => 0,
                "new york" or "nyc" or "est" => -5,
                "los angeles" or "la" or "pst" => -8,
                "tokyo" or "japan" => 9,
                "sydney" or "australia" => 11,
                "paris" or "france" or "berlin" or "germany" => 1,
                "dubai" or "uae" => 4,
                "india" or "mumbai" or "delhi" => 5.5,
                "china" or "beijing" or "shanghai" => 8,
                _ => null
            };
        }
        
        private static string? TryMathExpression(string input)
        {
            // Clean up the expression
            var expr = input.ToLower()
                .Replace("what's", "")
                .Replace("what is", "")
                .Replace("calculate", "")
                .Replace("×", "*")
                .Replace("÷", "/")
                .Replace("x", "*")
                .Trim();
            
            // Only process if it looks like math
            if (!Regex.IsMatch(expr, @"[\d\+\-\*\/\(\)\.\s]+"))
                return null;
            
            try
            {
                var table = new DataTable();
                var result = table.Compute(expr, "");
                return $"🔢 {expr} = **{result}**";
            }
            catch
            {
                return null;
            }
        }
    }
}
