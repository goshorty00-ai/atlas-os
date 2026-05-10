#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using AtlasAI.Memory.Models;

namespace AtlasAI.Memory.Tests
{
    /// <summary>
    /// Property-based tests for MemoryEntry serialization.
    /// Feature: project-memory-behavioral-learning, Property 1: Memory Entry Serialization Round-Trip
    /// Validates: Requirements 6.4
    /// </summary>
    public static class MemoryEntryPropertyTests
    {
        private static readonly Random _random = new();
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Property 1: Memory Entry Serialization Round-Trip
        /// </summary>
        public static (bool Success, string? FailingExample) TestSerializationRoundTrip(int iterations = 100)
        {
            for (int i = 0; i < iterations; i++)
            {
                var original = GenerateRandomMemoryEntry();
                var json = JsonSerializer.Serialize(original, _jsonOptions);
                var deserialized = JsonSerializer.Deserialize<MemoryEntry>(json, _jsonOptions);
                
                if (deserialized == null)
                {
                    return (false, $"Deserialization returned null for: {json}");
                }
                
                if (!original.Equals(deserialized))
                {
                    return (false, $"Round-trip failed.\nOriginal: {original}\nDeserialized: {deserialized}");
                }
            }
            return (true, null);
        }

        /// <summary>
        /// Property test: ProjectMemoryData round-trip serialization
        /// </summary>
        public static (bool Success, string? FailingExample) TestProjectMemoryDataRoundTrip(int iterations = 100)
        {
            for (int i = 0; i < iterations; i++)
            {
                var original = GenerateRandomProjectMemoryData();
                var json = JsonSerializer.Serialize(original, _jsonOptions);
                var deserialized = JsonSerializer.Deserialize<ProjectMemoryData>(json, _jsonOptions);
                
                if (deserialized == null)
                {
                    return (false, "Deserialization returned null for ProjectMemoryData");
                }
                
                if (original.Version != deserialized.Version ||
                    original.WorkspaceName != deserialized.WorkspaceName ||
                    original.Entries.Count != deserialized.Entries.Count)
                {
                    return (false, "ProjectMemoryData round-trip failed");
                }
                
                for (int j = 0; j < original.Entries.Count; j++)
                {
                    if (!original.Entries[j].Equals(deserialized.Entries[j]))
                    {
                        return (false, $"Entry {j} mismatch in ProjectMemoryData round-trip");
                    }
                }
            }
            return (true, null);
        }

        /// <summary>
        /// Property test: All enum values serialize correctly
        /// </summary>
        public static (bool Success, string? FailingExample) TestEnumSerialization()
        {
            foreach (MemoryEntryType type in Enum.GetValues<MemoryEntryType>())
            {
                var entry = new MemoryEntry { Type = type, Note = "Test", Confidence = 0.5 };
                var json = JsonSerializer.Serialize(entry, _jsonOptions);
                var deserialized = JsonSerializer.Deserialize<MemoryEntry>(json, _jsonOptions);
                
                if (deserialized?.Type != type)
                {
                    return (false, $"MemoryEntryType {type} failed round-trip");
                }
            }
            
            foreach (MemorySource source in Enum.GetValues<MemorySource>())
            {
                var entry = new MemoryEntry { Source = source, Note = "Test", Confidence = 0.5 };
                var json = JsonSerializer.Serialize(entry, _jsonOptions);
                var deserialized = JsonSerializer.Deserialize<MemoryEntry>(json, _jsonOptions);
                
                if (deserialized?.Source != source)
                {
                    return (false, $"MemorySource {source} failed round-trip");
                }
            }
            return (true, null);
        }

        private static MemoryEntry GenerateRandomMemoryEntry()
        {
            var types = Enum.GetValues<MemoryEntryType>();
            var sources = Enum.GetValues<MemorySource>();
            
            return new MemoryEntry
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Type = types[_random.Next(types.Length)],
                Source = sources[_random.Next(sources.Length)],
                Confidence = Math.Round(_random.NextDouble(), 4),
                Note = GenerateRandomString(1, 200),
                CreatedAt = GenerateRandomDateTime(),
                LastAppliedAt = _random.Next(2) == 0 ? null : GenerateRandomDateTime(),
                ApplyCount = _random.Next(0, 100)
            };
        }

        private static ProjectMemoryData GenerateRandomProjectMemoryData()
        {
            var entryCount = _random.Next(0, 20);
            var entries = new List<MemoryEntry>();
            for (int i = 0; i < entryCount; i++)
            {
                entries.Add(GenerateRandomMemoryEntry());
            }
            
            return new ProjectMemoryData
            {
                Version = "1.0",
                WorkspaceName = GenerateRandomString(5, 30),
                CreatedAt = GenerateRandomDateTime(),
                LastModifiedAt = GenerateRandomDateTime(),
                Entries = entries
            };
        }

        private static string GenerateRandomString(int minLength, int maxLength)
        {
            var length = _random.Next(minLength, maxLength + 1);
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 _-.";
            return new string(Enumerable.Range(0, length).Select(_ => chars[_random.Next(chars.Length)]).ToArray());
        }

        private static DateTime GenerateRandomDateTime()
        {
            var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var range = (DateTime.UtcNow - start).Days;
            return start.AddDays(_random.Next(range)).AddSeconds(_random.Next(86400));
        }
    }

    /// <summary>
    /// Test runner for memory property tests.
    /// </summary>
    public static class MemoryPropertyTestRunner
    {
        public static List<(string TestName, bool Success, string? FailingExample)> RunAllTests()
        {
            var results = new List<(string TestName, bool Success, string? FailingExample)>();
            
            System.Diagnostics.Debug.WriteLine("[MemoryPropertyTests] Running property tests...");
            
            var result1 = MemoryEntryPropertyTests.TestSerializationRoundTrip(100);
            results.Add(("Property 1: MemoryEntry Serialization Round-Trip", result1.Success, result1.FailingExample));
            LogResult("Property 1: MemoryEntry Serialization Round-Trip", result1);
            
            var result1b = MemoryEntryPropertyTests.TestProjectMemoryDataRoundTrip(100);
            results.Add(("Property 1b: ProjectMemoryData Serialization Round-Trip", result1b.Success, result1b.FailingExample));
            LogResult("Property 1b: ProjectMemoryData Serialization Round-Trip", result1b);
            
            var result2 = MemoryEntryPropertyTests.TestEnumSerialization();
            results.Add(("Enum Serialization", result2.Success, result2.FailingExample));
            LogResult("Enum Serialization", result2);
            
            var allPassed = results.All(r => r.Success);
            System.Diagnostics.Debug.WriteLine($"[MemoryPropertyTests] {(allPassed ? "ALL TESTS PASSED" : "SOME TESTS FAILED")}");
            
            return results;
        }

        private static void LogResult(string testName, (bool Success, string? FailingExample) result)
        {
            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"  ✓ {testName}: PASSED");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"  ✗ {testName}: FAILED - {result.FailingExample}");
            }
        }
    }
}
