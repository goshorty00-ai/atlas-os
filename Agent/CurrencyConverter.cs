using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Currency Converter - Convert between currencies using live rates.
    /// </summary>
    public static class CurrencyConverter
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
        
        private static readonly Dictionary<string, string> CurrencyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "usd", "US Dollar" }, { "dollar", "USD" }, { "dollars", "USD" },
            { "eur", "Euro" }, { "euro", "EUR" }, { "euros", "EUR" },
            { "gbp", "British Pound" }, { "pound", "GBP" }, { "pounds", "GBP" }, { "quid", "GBP" },
            { "jpy", "Japanese Yen" }, { "yen", "JPY" },
            { "cny", "Chinese Yuan" }, { "yuan", "CNY" }, { "rmb", "CNY" },
            { "inr", "Indian Rupee" }, { "rupee", "INR" }, { "rupees", "INR" },
            { "aud", "Australian Dollar" },
            { "cad", "Canadian Dollar" },
            { "chf", "Swiss Franc" }, { "franc", "CHF" },
            { "krw", "South Korean Won" }, { "won", "KRW" },
            { "mxn", "Mexican Peso" }, { "peso", "MXN" },
            { "brl", "Brazilian Real" }, { "real", "BRL" },
            { "rub", "Russian Ruble" }, { "ruble", "RUB" },
            { "sek", "Swedish Krona" },
            { "nok", "Norwegian Krone" },
            { "dkk", "Danish Krone" },
            { "pln", "Polish Zloty" },
            { "thb", "Thai Baht" }, { "baht", "THB" },
            { "sgd", "Singapore Dollar" },
            { "hkd", "Hong Kong Dollar" },
            { "nzd", "New Zealand Dollar" },
            { "zar", "South African Rand" }, { "rand", "ZAR" },
            { "try", "Turkish Lira" }, { "lira", "TRY" },
            { "btc", "Bitcoin" }, { "bitcoin", "BTC" },
            { "eth", "Ethereum" }, { "ethereum", "ETH" },
        };
        
        /// <summary>
        /// Handle currency conversion commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Pattern: "100 usd to eur" or "convert 50 dollars to pounds"
            var match = Regex.Match(lower, @"(?:convert\s+)?(\d+(?:\.\d+)?)\s*(\w+)\s+(?:to|in)\s+(\w+)");
            if (match.Success)
            {
                var amount = decimal.Parse(match.Groups[1].Value);
                var fromCurrency = NormalizeCurrency(match.Groups[2].Value);
                var toCurrency = NormalizeCurrency(match.Groups[3].Value);
                
                if (fromCurrency != null && toCurrency != null)
                    return await ConvertCurrencyAsync(amount, fromCurrency, toCurrency);
            }
            
            // Pattern: "how much is 100 usd in eur"
            match = Regex.Match(lower, @"how much is\s+(\d+(?:\.\d+)?)\s*(\w+)\s+in\s+(\w+)");
            if (match.Success)
            {
                var amount = decimal.Parse(match.Groups[1].Value);
                var fromCurrency = NormalizeCurrency(match.Groups[2].Value);
                var toCurrency = NormalizeCurrency(match.Groups[3].Value);
                
                if (fromCurrency != null && toCurrency != null)
                    return await ConvertCurrencyAsync(amount, fromCurrency, toCurrency);
            }
            
            // Exchange rate query
            if (lower.Contains("exchange rate") || lower.Contains("rate for"))
            {
                match = Regex.Match(lower, @"(\w+)\s+(?:to|\/)\s*(\w+)");
                if (match.Success)
                {
                    var fromCurrency = NormalizeCurrency(match.Groups[1].Value);
                    var toCurrency = NormalizeCurrency(match.Groups[2].Value);
                    
                    if (fromCurrency != null && toCurrency != null)
                        return await GetExchangeRateAsync(fromCurrency, toCurrency);
                }
            }
            
            return null;
        }
        
        private static string? NormalizeCurrency(string input)
        {
            input = input.ToLower().Trim();
            
            // Direct 3-letter code
            if (input.Length == 3 && CurrencyNames.ContainsKey(input))
                return input.ToUpper();
            
            // Common names
            if (CurrencyNames.TryGetValue(input, out var code))
            {
                if (code.Length == 3)
                    return code;
                return NormalizeCurrency(code);
            }
            
            return null;
        }
        
        private static async Task<string> ConvertCurrencyAsync(decimal amount, string from, string to)
        {
            try
            {
                // Using exchangerate-api.com free tier
                var response = await _httpClient.GetStringAsync($"https://api.exchangerate-api.com/v4/latest/{from}");
                using var doc = JsonDocument.Parse(response);
                
                var rates = doc.RootElement.GetProperty("rates");
                if (!rates.TryGetProperty(to, out var rateElement))
                    return $"❌ Currency not found: {to}";
                
                var rate = rateElement.GetDecimal();
                var result = amount * rate;
                
                var fromName = GetCurrencyName(from);
                var toName = GetCurrencyName(to);
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(result.ToString("F2")));
                
                return $"💱 **Currency Conversion:**\n\n" +
                       $"**{amount:N2} {from}** ({fromName})\n" +
                       $"= **{result:N2} {to}** ({toName})\n\n" +
                       $"Rate: 1 {from} = {rate:N4} {to}\n" +
                       $"✓ Result copied to clipboard!";
            }
            catch (Exception ex)
            {
                return $"❌ Conversion error: {ex.Message}";
            }
        }
        
        private static async Task<string> GetExchangeRateAsync(string from, string to)
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"https://api.exchangerate-api.com/v4/latest/{from}");
                using var doc = JsonDocument.Parse(response);
                
                var rates = doc.RootElement.GetProperty("rates");
                if (!rates.TryGetProperty(to, out var rateElement))
                    return $"❌ Currency not found: {to}";
                
                var rate = rateElement.GetDecimal();
                var inverse = 1 / rate;
                
                return $"💱 **Exchange Rate:**\n\n" +
                       $"1 {from} = {rate:N4} {to}\n" +
                       $"1 {to} = {inverse:N4} {from}";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }
        
        private static string GetCurrencyName(string code)
        {
            foreach (var kvp in CurrencyNames)
            {
                if (kvp.Key.Equals(code, StringComparison.OrdinalIgnoreCase) && kvp.Value.Length > 3)
                    return kvp.Value;
            }
            return code;
        }
    }
}
