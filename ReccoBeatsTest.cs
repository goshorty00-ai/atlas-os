using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;

namespace ReccoBeatsTest
{
    class Program
    {
        private static readonly HttpClient Http = new HttpClient();

        static async Task Main(string[] args)
        {
            Console.WriteLine("Testing ReccoBeats API Endpoints...");

            // 1. Test Album Search
            Console.WriteLine("\n1. Testing Album Search (query='Thriller')...");
            await TestEndpoint("https://api.reccobeats.com/v1/album/search?searchText=Thriller&size=5");

            // 2. Test Track Search (The one I'm unsure about)
            Console.WriteLine("\n2. Testing Track Search (query='Billie Jean')...");
            // Try the one in my code
            await TestEndpoint("https://api.reccobeats.com/v1/track/search?query=Billie%20Jean&limit=5");
            
            // Try alternative if that fails?
            // await TestEndpoint("https://api.reccobeats.com/v1/search?type=track&q=Billie%20Jean");

            // 3. Test Track Details (Need a valid ID from search if possible, otherwise try a known one or skip)
            // I'll try to extract an ID from the track search result if it works.
        }

        static async Task<string> TestEndpoint(string url)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var res = await Http.SendAsync(req);
                Console.WriteLine($"Status: {res.StatusCode}");
                
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response: {json.Substring(0, Math.Min(500, json.Length))}..."); // Print first 500 chars
                    return json;
                }
                else
                {
                    Console.WriteLine($"Error: {res.ReasonPhrase}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                return null;
            }
        }
    }
}
