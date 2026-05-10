using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.Tools;

public static class MacroSanityRunner
{
    public static string GetReportPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AtlasAI",
        "macro_sanity_report.txt");

    private static string GetMacrosPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AtlasAI",
        "macros.json");

    public static async Task<string> RunAsync(CancellationToken ct)
    {
        var reportPath = GetReportPath();
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? ".");

        try
        {
            File.WriteAllText(reportPath, "Atlas Macro Sanity Report\n(status: starting)\nUTC: " + DateTime.UtcNow.ToString("O") + "\n");
        }
        catch
        {
        }

        var macro1 = "__sanity__" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_1";
        var macro2 = "__sanity__" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_risky";

        var tempDir = Path.GetTempPath();
        var tempFile = Path.Combine(tempDir, "AtlasAI_Sanity.txt");
        var renamedName = "AtlasAI_Sanity_Renamed.txt";
        var renamedFile = Path.Combine(tempDir, renamedName);

        var sb = new StringBuilder();
        sb.AppendLine("Atlas Macro Sanity Report");
        sb.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
        sb.AppendLine("ReportPath: " + reportPath);
        sb.AppendLine("MacrosPath: " + GetMacrosPath());
        sb.AppendLine("TempFile: " + tempFile);
        sb.AppendLine();

        try
        {
            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(tempFile, "AtlasAI sanity file\nUTC=" + DateTime.UtcNow.ToString("O"));
                if (File.Exists(renamedFile)) File.Delete(renamedFile);
            }
            catch
            {
            }

            sb.AppendLine("[1] Define a safe macro");
            var r1 = await ToolExecutor.TryExecuteToolWithCancellationAsync(
                $"define macro {macro1}: hostname; whoami",
                ct);
            sb.AppendLine(r1 ?? "(null)");
            sb.AppendLine();

            sb.AppendLine("[2] Run the safe macro");
            var r2 = await ToolExecutor.TryExecuteToolWithCancellationAsync(
                $"run macro {macro1}",
                ct);
            sb.AppendLine(r2 ?? "(null)");
            sb.AppendLine();

            sb.AppendLine("[3] Define a risky macro (should require confirm)");
            var r3 = await ToolExecutor.TryExecuteToolWithCancellationAsync(
                $"define macro {macro2}: rename \"{tempFile}\" to {renamedName}",
                ct);
            sb.AppendLine(r3 ?? "(null)");
            sb.AppendLine();

            sb.AppendLine("[4] Run the risky macro (expect preview + confirm prompt)");
            var r4 = await ToolExecutor.TryExecuteToolWithCancellationAsync(
                $"run macro {macro2}",
                ct);
            sb.AppendLine(r4 ?? "(null)");
            sb.AppendLine();

            sb.AppendLine("[5] Confirm pending macro (should execute steps through pipeline)");
            var r5 = await ToolExecutor.TryExecuteToolWithCancellationAsync(
                "confirm",
                ct);
            sb.AppendLine(r5 ?? "(null)");
            sb.AppendLine();
        }
        catch (OperationCanceledException)
        {
            sb.AppendLine("❌ Cancelled.");
        }
        catch (Exception ex)
        {
            sb.AppendLine("❌ Runner failed: " + ex.Message);
        }
        finally
        {
            try
            {
                CleanupSanityMacros(new[] { macro1, macro2 });
            }
            catch (Exception ex)
            {
                sb.AppendLine("(Cleanup failed: " + ex.Message + ")");
            }

            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                if (File.Exists(renamedFile)) File.Delete(renamedFile);
            }
            catch
            {
            }

            try
            {
                File.WriteAllText(reportPath, sb.ToString());
            }
            catch
            {
                try
                {
                    var fallback = Path.Combine(Path.GetTempPath(), "AtlasAI_macro_sanity_report.txt");
                    File.WriteAllText(fallback, sb.ToString());
                    reportPath = fallback;
                }
                catch
                {
                }
            }
        }

        return reportPath;
    }

    private static void CleanupSanityMacros(IEnumerable<string> names)
    {
        var path = GetMacrosPath();
        if (!File.Exists(path))
            return;

        var nameSet = new HashSet<string>(names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        if (nameSet.Count == 0)
            return;

        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = JsonDocument.Parse(fs);

                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return;

                var kept = new List<JsonElement>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object)
                    {
                        kept.Add(el);
                        continue;
                    }

                    if (el.TryGetProperty("name", out var nameProp))
                    {
                        var name = (nameProp.GetString() ?? "").Trim();
                        if (nameSet.Contains(name))
                            continue;
                    }

                    kept.Add(el);
                }

                var options = new JsonWriterOptions { Indented = true };
                using var outStream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new Utf8JsonWriter(outStream, options);
                writer.WriteStartArray();
                foreach (var el in kept)
                    el.WriteTo(writer);
                writer.WriteEndArray();
                return;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(200 + (attempt * 50));
            }
            catch (UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(200 + (attempt * 50));
            }
        }
    }
}
