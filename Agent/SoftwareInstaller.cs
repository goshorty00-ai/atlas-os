using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Software installation capabilities - like Kiro, can install anything the user asks for
    /// Supports: winget, pip, npm, choco, direct downloads
    /// </summary>
    public static class SoftwareInstaller
    {
        // Common software mappings (what users say -> what to install)
        private static readonly Dictionary<string, SoftwareInfo> KnownSoftware = new()
        {
            // Development
            { "python", new("Python.Python.3.12", "winget", "Python programming language") },
            { "python3", new("Python.Python.3.12", "winget", "Python programming language") },
            { "node", new("OpenJS.NodeJS.LTS", "winget", "Node.js runtime") },
            { "nodejs", new("OpenJS.NodeJS.LTS", "winget", "Node.js runtime") },
            { "git", new("Git.Git", "winget", "Git version control") },
            { "vscode", new("Microsoft.VisualStudioCode", "winget", "Visual Studio Code") },
            { "visual studio code", new("Microsoft.VisualStudioCode", "winget", "Visual Studio Code") },
            { "dotnet", new("Microsoft.DotNet.SDK.8", "winget", ".NET 8 SDK") },
            { ".net", new("Microsoft.DotNet.SDK.8", "winget", ".NET 8 SDK") },
            { "rust", new("Rustlang.Rust.MSVC", "winget", "Rust programming language") },
            { "go", new("GoLang.Go", "winget", "Go programming language") },
            { "java", new("Microsoft.OpenJDK.21", "winget", "Java JDK 21") },
            { "jdk", new("Microsoft.OpenJDK.21", "winget", "Java JDK 21") },
            
            // Browsers
            { "chrome", new("Google.Chrome", "winget", "Google Chrome browser") },
            { "firefox", new("Mozilla.Firefox", "winget", "Mozilla Firefox browser") },
            { "edge", new("Microsoft.Edge", "winget", "Microsoft Edge browser") },
            { "brave", new("Brave.Brave", "winget", "Brave browser") },
            
            // Communication
            { "discord", new("Discord.Discord", "winget", "Discord chat app") },
            { "slack", new("SlackTechnologies.Slack", "winget", "Slack messaging") },
            { "zoom", new("Zoom.Zoom", "winget", "Zoom video conferencing") },
            { "teams", new("Microsoft.Teams", "winget", "Microsoft Teams") },
            { "telegram", new("Telegram.TelegramDesktop", "winget", "Telegram messenger") },
            
            // Media
            { "spotify", new("Spotify.Spotify", "winget", "Spotify music player") },
            { "vlc", new("VideoLAN.VLC", "winget", "VLC media player") },
            { "obs", new("OBSProject.OBSStudio", "winget", "OBS Studio for streaming") },
            { "audacity", new("Audacity.Audacity", "winget", "Audacity audio editor") },
            
            // Utilities
            { "7zip", new("7zip.7zip", "winget", "7-Zip file archiver") },
            { "7-zip", new("7zip.7zip", "winget", "7-Zip file archiver") },
            { "notepad++", new("Notepad++.Notepad++", "winget", "Notepad++ text editor") },
            { "powertoys", new("Microsoft.PowerToys", "winget", "Microsoft PowerToys") },
            { "everything", new("voidtools.Everything", "winget", "Everything file search") },
            { "winrar", new("RARLab.WinRAR", "winget", "WinRAR archiver") },
            
            // Gaming
            { "steam", new("Valve.Steam", "winget", "Steam gaming platform") },
            { "epic games", new("EpicGames.EpicGamesLauncher", "winget", "Epic Games Launcher") },
            
            // Python packages (pip)
            { "edge-tts", new("edge-tts", "pip", "Microsoft Edge TTS") },
            { "edgetts", new("edge-tts", "pip", "Microsoft Edge TTS") },
            { "numpy", new("numpy", "pip", "NumPy for Python") },
            { "pandas", new("pandas", "pip", "Pandas data analysis") },
            { "requests", new("requests", "pip", "Python HTTP library") },
            { "flask", new("flask", "pip", "Flask web framework") },
            { "django", new("django", "pip", "Django web framework") },
            { "pytorch", new("torch", "pip", "PyTorch ML framework") },
            { "tensorflow", new("tensorflow", "pip", "TensorFlow ML framework") },
            { "openai", new("openai", "pip", "OpenAI Python SDK") },
            { "anthropic", new("anthropic", "pip", "Anthropic Claude SDK") },
            
            // Node packages (npm global)
            { "typescript", new("typescript", "npm", "TypeScript compiler") },
            { "yarn", new("yarn", "npm", "Yarn package manager") },
            { "pnpm", new("pnpm", "npm", "PNPM package manager") },
            { "create-react-app", new("create-react-app", "npm", "Create React App") },
            { "vite", new("create-vite", "npm", "Vite build tool") },
            { "next", new("create-next-app", "npm", "Next.js framework") },
        };

        /// <summary>
        /// Install software by name - figures out the best way to install it
        /// </summary>
        public static async Task<string> InstallAsync(string softwareName)
        {
            var name = softwareName.ToLower().Trim();
            Debug.WriteLine($"[Installer] Installing: {name}");

            // Check if we know this software
            if (KnownSoftware.TryGetValue(name, out var info))
            {
                return await InstallWithPackageManagerAsync(info);
            }

            // Try to find it with winget search
            var searchResult = await SearchWingetAsync(name);
            if (!string.IsNullOrEmpty(searchResult))
            {
                return await InstallWithWingetAsync(searchResult, name);
            }

            // Try pip if it looks like a Python package
            if (await IsPythonInstalledAsync())
            {
                var pipResult = await TryPipInstallAsync(name);
                if (pipResult != null)
                    return pipResult;
            }

            // Try npm if it looks like a Node package
            if (await IsNodeInstalledAsync())
            {
                var npmResult = await TryNpmInstallAsync(name);
                if (npmResult != null)
                    return npmResult;
            }

            return $"❌ Couldn't find '{softwareName}'. Try being more specific or check the exact package name.";
        }

        private static async Task<string> InstallWithPackageManagerAsync(SoftwareInfo info)
        {
            return info.Manager switch
            {
                "winget" => await InstallWithWingetAsync(info.PackageId, info.Description),
                "pip" => await InstallWithPipAsync(info.PackageId, info.Description),
                "npm" => await InstallWithNpmAsync(info.PackageId, info.Description),
                "choco" => await InstallWithChocoAsync(info.PackageId, info.Description),
                _ => $"❌ Unknown package manager: {info.Manager}"
            };
        }

        private static async Task<string> InstallWithWingetAsync(string packageId, string description)
        {
            Debug.WriteLine($"[Installer] winget install {packageId}");
            
            var result = await RunCommandAsync("winget", $"install --id {packageId} --accept-source-agreements --accept-package-agreements -e");
            
            if (result.ExitCode == 0 || result.Output.Contains("Successfully installed") || result.Output.Contains("already installed"))
            {
                return $"✅ **Installed {description}**\n\n{GetPostInstallMessage(packageId)}";
            }
            
            // Check if already installed
            if (result.Output.Contains("already installed") || result.Output.Contains("No available upgrade"))
            {
                return $"✅ **{description}** is already installed!";
            }

            return $"❌ Failed to install {description}:\n```\n{result.Output}\n```";
        }

        private static async Task<string> InstallWithPipAsync(string packageName, string description)
        {
            Debug.WriteLine($"[Installer] pip install {packageName}");
            
            // First check if Python is installed
            if (!await IsPythonInstalledAsync())
            {
                return "❌ Python is not installed. Say 'install python' first!";
            }

            var result = await RunCommandAsync("pip", $"install {packageName}");
            
            if (result.ExitCode == 0 || result.Output.Contains("Successfully installed") || result.Output.Contains("already satisfied"))
            {
                return $"✅ **Installed {description}** (Python package)\n\nYou can now use it with: `import {packageName.Replace("-", "_")}`";
            }

            return $"❌ Failed to install {description}:\n```\n{result.Output}\n```";
        }

        private static async Task<string> InstallWithNpmAsync(string packageName, string description)
        {
            Debug.WriteLine($"[Installer] npm install -g {packageName}");
            
            if (!await IsNodeInstalledAsync())
            {
                return "❌ Node.js is not installed. Say 'install node' first!";
            }

            var result = await RunCommandAsync("npm", $"install -g {packageName}");
            
            if (result.ExitCode == 0 || result.Output.Contains("added"))
            {
                return $"✅ **Installed {description}** (npm global package)";
            }

            return $"❌ Failed to install {description}:\n```\n{result.Output}\n```";
        }

        private static async Task<string> InstallWithChocoAsync(string packageName, string description)
        {
            Debug.WriteLine($"[Installer] choco install {packageName}");
            
            var result = await RunCommandAsync("choco", $"install {packageName} -y");
            
            if (result.ExitCode == 0)
            {
                return $"✅ **Installed {description}** (Chocolatey)";
            }

            return $"❌ Failed to install {description}. Chocolatey may not be installed.";
        }

        private static async Task<string?> SearchWingetAsync(string query)
        {
            var result = await RunCommandAsync("winget", $"search {query} --accept-source-agreements");
            
            if (result.ExitCode != 0 || string.IsNullOrEmpty(result.Output))
                return null;

            // Parse winget output to find best match
            var lines = result.Output.Split('\n').Skip(2); // Skip header
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                // Extract package ID (usually second column)
                var parts = Regex.Split(line.Trim(), @"\s{2,}");
                if (parts.Length >= 2)
                {
                    var packageId = parts[1].Trim();
                    if (!string.IsNullOrEmpty(packageId) && !packageId.Contains(" "))
                    {
                        return packageId;
                    }
                }
            }

            return null;
        }

        private static async Task<string?> TryPipInstallAsync(string name)
        {
            // Check if package exists on PyPI
            var result = await RunCommandAsync("pip", $"index versions {name}", timeoutMs: 10000);
            if (result.ExitCode == 0 && !result.Output.Contains("ERROR"))
            {
                return await InstallWithPipAsync(name, name);
            }
            return null;
        }

        private static async Task<string?> TryNpmInstallAsync(string name)
        {
            // Check if package exists on npm
            var result = await RunCommandAsync("npm", $"view {name} version", timeoutMs: 10000);
            if (result.ExitCode == 0 && !string.IsNullOrEmpty(result.Output))
            {
                return await InstallWithNpmAsync(name, name);
            }
            return null;
        }

        private static async Task<bool> IsPythonInstalledAsync()
        {
            var result = await RunCommandAsync("python", "--version", timeoutMs: 5000);
            return result.ExitCode == 0;
        }

        private static async Task<bool> IsNodeInstalledAsync()
        {
            var result = await RunCommandAsync("node", "--version", timeoutMs: 5000);
            return result.ExitCode == 0;
        }

        private static string GetPostInstallMessage(string packageId)
        {
            return packageId.ToLower() switch
            {
                var p when p.Contains("python") => "🐍 Python installed! You may need to restart your terminal.\nTry: `python --version`",
                var p when p.Contains("node") => "📦 Node.js installed! You may need to restart your terminal.\nTry: `node --version`",
                var p when p.Contains("git") => "🔧 Git installed!\nTry: `git --version`",
                var p when p.Contains("vscode") || p.Contains("visualstudiocode") => "💻 VS Code installed! Launch it from Start menu or type `code` in terminal.",
                var p when p.Contains("discord") => "💬 Discord installed! Launch it from Start menu.",
                var p when p.Contains("spotify") => "🎵 Spotify installed! Launch it from Start menu.",
                var p when p.Contains("chrome") => "🌐 Chrome installed! Launch it from Start menu.",
                _ => "You may need to restart your terminal for changes to take effect."
            };
        }

        /// <summary>
        /// Check if software is installed
        /// </summary>
        public static async Task<bool> IsInstalledAsync(string softwareName)
        {
            var name = softwareName.ToLower().Trim();
            
            // Check common commands
            var commands = new Dictionary<string, string>
            {
                { "python", "python --version" },
                { "node", "node --version" },
                { "git", "git --version" },
                { "dotnet", "dotnet --version" },
                { "rust", "rustc --version" },
                { "go", "go version" },
                { "java", "java --version" },
            };

            if (commands.TryGetValue(name, out var cmd))
            {
                var parts = cmd.Split(' ', 2);
                var result = await RunCommandAsync(parts[0], parts.Length > 1 ? parts[1] : "", timeoutMs: 5000);
                return result.ExitCode == 0;
            }

            // Check winget list
            var wingetResult = await RunCommandAsync("winget", $"list --name {softwareName}", timeoutMs: 10000);
            return wingetResult.Output.Contains(softwareName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Uninstall software
        /// </summary>
        public static async Task<string> UninstallAsync(string softwareName)
        {
            var name = softwareName.ToLower().Trim();
            
            if (KnownSoftware.TryGetValue(name, out var info))
            {
                if (info.Manager == "winget")
                {
                    var result = await RunCommandAsync("winget", $"uninstall --id {info.PackageId}");
                    if (result.ExitCode == 0)
                        return $"✅ Uninstalled {info.Description}";
                    return $"❌ Failed to uninstall: {result.Output}";
                }
                else if (info.Manager == "pip")
                {
                    var result = await RunCommandAsync("pip", $"uninstall {info.PackageId} -y");
                    if (result.ExitCode == 0)
                        return $"✅ Uninstalled {info.Description}";
                    return $"❌ Failed to uninstall: {result.Output}";
                }
            }

            // Try winget uninstall with name
            var wingetResult = await RunCommandAsync("winget", $"uninstall --name \"{softwareName}\"");
            if (wingetResult.ExitCode == 0)
                return $"✅ Uninstalled {softwareName}";

            return $"❌ Couldn't find {softwareName} to uninstall";
        }

        private static async Task<CommandResult> RunCommandAsync(string command, string args, int timeoutMs = 120000)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                var output = new System.Text.StringBuilder();
                var error = new System.Text.StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var completed = await Task.Run(() => process.WaitForExit(timeoutMs));
                
                if (!completed)
                {
                    process.Kill();
                    return new CommandResult { ExitCode = -1, Output = "Command timed out" };
                }

                var fullOutput = output.ToString();
                if (error.Length > 0)
                    fullOutput += "\n" + error.ToString();

                return new CommandResult { ExitCode = process.ExitCode, Output = fullOutput };
            }
            catch (Exception ex)
            {
                return new CommandResult { ExitCode = -1, Output = ex.Message };
            }
        }

        private class CommandResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; } = "";
        }

        private class SoftwareInfo
        {
            public string PackageId { get; }
            public string Manager { get; }
            public string Description { get; }

            public SoftwareInfo(string packageId, string manager, string description)
            {
                PackageId = packageId;
                Manager = manager;
                Description = description;
            }
        }
    }
}
