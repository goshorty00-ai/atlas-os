using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AtlasAI.Tools
{
    /// <summary>
    /// Comprehensive Windows knowledge base - all settings URIs, commands, and paths
    /// Makes Atlas truly intelligent about Windows
    /// </summary>
    public static class WindowsKnowledgeBase
    {
        #region Windows Settings URIs (ms-settings:)
        
        /// <summary>
        /// Complete mapping of Windows Settings pages to their ms-settings: URIs
        /// </summary>
        public static readonly Dictionary<string, string> SettingsPages = new(StringComparer.OrdinalIgnoreCase)
        {
            // === SYSTEM ===
            ["display"] = "ms-settings:display",
            ["screen"] = "ms-settings:display",
            ["monitor"] = "ms-settings:display",
            ["resolution"] = "ms-settings:display",
            ["brightness"] = "ms-settings:display",
            ["night light"] = "ms-settings:nightlight",
            ["nightlight"] = "ms-settings:nightlight",
            ["blue light"] = "ms-settings:nightlight",
            ["sound"] = "ms-settings:sound",
            ["audio"] = "ms-settings:sound",
            ["volume"] = "ms-settings:sound",
            ["speakers"] = "ms-settings:sound",
            ["microphone"] = "ms-settings:sound",
            ["notifications"] = "ms-settings:notifications",
            ["focus assist"] = "ms-settings:quiethours",
            ["focus"] = "ms-settings:quiethours",
            ["do not disturb"] = "ms-settings:quiethours",
            ["power"] = "ms-settings:powersleep",
            ["battery"] = "ms-settings:batterysaver",
            ["sleep"] = "ms-settings:powersleep",
            ["storage"] = "ms-settings:storagesense",
            ["disk"] = "ms-settings:storagesense",
            ["multitasking"] = "ms-settings:multitasking",
            ["snap"] = "ms-settings:multitasking",
            ["virtual desktops"] = "ms-settings:multitasking",
            ["activation"] = "ms-settings:activation",
            ["troubleshoot"] = "ms-settings:troubleshoot",
            ["recovery"] = "ms-settings:recovery",
            ["projection"] = "ms-settings:project",
            ["remote desktop"] = "ms-settings:remotedesktop",
            ["clipboard"] = "ms-settings:clipboard",
            ["about"] = "ms-settings:about",
            ["system info"] = "ms-settings:about",
            ["pc info"] = "ms-settings:about",
            ["computer info"] = "ms-settings:about",
            ["device name"] = "ms-settings:about",
            
            // === BLUETOOTH & DEVICES ===
            ["bluetooth"] = "ms-settings:bluetooth",
            ["devices"] = "ms-settings:connecteddevices",
            ["printers"] = "ms-settings:printers",
            ["printer"] = "ms-settings:printers",
            ["scanners"] = "ms-settings:printers",
            ["mouse"] = "ms-settings:mousetouchpad",
            ["touchpad"] = "ms-settings:mousetouchpad",
            ["pen"] = "ms-settings:pen",
            ["stylus"] = "ms-settings:pen",
            ["autoplay"] = "ms-settings:autoplay",
            ["usb"] = "ms-settings:usb",
            
            // === NETWORK & INTERNET ===
            ["wifi"] = "ms-settings:network-wifi",
            ["wi-fi"] = "ms-settings:network-wifi",
            ["wireless"] = "ms-settings:network-wifi",
            ["ethernet"] = "ms-settings:network-ethernet",
            ["wired"] = "ms-settings:network-ethernet",
            ["vpn"] = "ms-settings:network-vpn",
            ["mobile hotspot"] = "ms-settings:network-mobilehotspot",
            ["hotspot"] = "ms-settings:network-mobilehotspot",
            ["airplane mode"] = "ms-settings:network-airplanemode",
            ["airplane"] = "ms-settings:network-airplanemode",
            ["flight mode"] = "ms-settings:network-airplanemode",
            ["proxy"] = "ms-settings:network-proxy",
            ["dial-up"] = "ms-settings:network-dialup",
            ["network"] = "ms-settings:network-status",
            ["internet"] = "ms-settings:network-status",
            ["connection"] = "ms-settings:network-status",
            
            // === PERSONALIZATION ===
            ["background"] = "ms-settings:personalization-background",
            ["wallpaper"] = "ms-settings:personalization-background",
            ["desktop background"] = "ms-settings:personalization-background",
            ["colors"] = "ms-settings:personalization-colors",
            ["accent color"] = "ms-settings:personalization-colors",
            ["theme"] = "ms-settings:themes",
            ["themes"] = "ms-settings:themes",
            ["lock screen"] = "ms-settings:lockscreen",
            ["lockscreen"] = "ms-settings:lockscreen",
            ["fonts"] = "ms-settings:fonts",
            ["start menu"] = "ms-settings:personalization-start",
            ["start"] = "ms-settings:personalization-start",
            ["taskbar"] = "ms-settings:taskbar",
            
            // === APPS ===
            ["apps"] = "ms-settings:appsfeatures",
            ["programs"] = "ms-settings:appsfeatures",
            ["installed apps"] = "ms-settings:appsfeatures",
            ["default apps"] = "ms-settings:defaultapps",
            ["defaults"] = "ms-settings:defaultapps",
            ["default browser"] = "ms-settings:defaultapps",
            ["default programs"] = "ms-settings:defaultapps",
            ["file associations"] = "ms-settings:defaultapps",
            ["offline maps"] = "ms-settings:maps",
            ["maps"] = "ms-settings:maps",
            ["optional features"] = "ms-settings:optionalfeatures",
            ["startup apps"] = "ms-settings:startupapps",
            ["startup"] = "ms-settings:startupapps",
            
            // === ACCOUNTS ===
            ["account"] = "ms-settings:yourinfo",
            ["your info"] = "ms-settings:yourinfo",
            ["profile"] = "ms-settings:yourinfo",
            ["email"] = "ms-settings:emailandaccounts",
            ["sign-in options"] = "ms-settings:signinoptions",
            ["sign in"] = "ms-settings:signinoptions",
            ["login"] = "ms-settings:signinoptions",
            ["password"] = "ms-settings:signinoptions",
            ["pin"] = "ms-settings:signinoptions",
            ["windows hello"] = "ms-settings:signinoptions",
            ["fingerprint"] = "ms-settings:signinoptions",
            ["face recognition"] = "ms-settings:signinoptions",
            ["family"] = "ms-settings:family",
            ["other users"] = "ms-settings:otherusers",
            ["sync"] = "ms-settings:sync",
            ["backup"] = "ms-settings:backup",
            
            // === TIME & LANGUAGE ===
            ["date"] = "ms-settings:dateandtime",
            ["time"] = "ms-settings:dateandtime",
            ["date and time"] = "ms-settings:dateandtime",
            ["timezone"] = "ms-settings:dateandtime",
            ["time zone"] = "ms-settings:dateandtime",
            ["language"] = "ms-settings:regionlanguage",
            ["region"] = "ms-settings:regionlanguage",
            ["speech"] = "ms-settings:speech",
            ["voice"] = "ms-settings:speech",
            ["typing"] = "ms-settings:typing",
            
            // === GAMING ===
            ["game bar"] = "ms-settings:gaming-gamebar",
            ["gamebar"] = "ms-settings:gaming-gamebar",
            ["xbox game bar"] = "ms-settings:gaming-gamebar",
            ["captures"] = "ms-settings:gaming-gamedvr",
            ["game dvr"] = "ms-settings:gaming-gamedvr",
            ["game mode"] = "ms-settings:gaming-gamemode",
            ["gamemode"] = "ms-settings:gaming-gamemode",
            ["gaming"] = "ms-settings:gaming-gamemode",
            
            // === ACCESSIBILITY ===
            ["accessibility"] = "ms-settings:easeofaccess",
            ["ease of access"] = "ms-settings:easeofaccess",
            ["text size"] = "ms-settings:easeofaccess-display",
            ["magnifier"] = "ms-settings:easeofaccess-magnifier",
            ["color filters"] = "ms-settings:easeofaccess-colorfilter",
            ["colorblind"] = "ms-settings:easeofaccess-colorfilter",
            ["high contrast"] = "ms-settings:easeofaccess-highcontrast",
            ["narrator"] = "ms-settings:easeofaccess-narrator",
            ["screen reader"] = "ms-settings:easeofaccess-narrator",
            ["closed captions"] = "ms-settings:easeofaccess-closedcaptioning",
            ["captions"] = "ms-settings:easeofaccess-closedcaptioning",
            ["keyboard accessibility"] = "ms-settings:easeofaccess-keyboard",
            ["sticky keys"] = "ms-settings:easeofaccess-keyboard",
            ["filter keys"] = "ms-settings:easeofaccess-keyboard",
            ["mouse accessibility"] = "ms-settings:easeofaccess-mouse",
            ["eye control"] = "ms-settings:easeofaccess-eyecontrol",
            
            // === PRIVACY & SECURITY ===
            ["privacy"] = "ms-settings:privacy",
            ["security"] = "ms-settings:privacy",
            ["windows security"] = "ms-settings:windowsdefender",
            ["defender"] = "ms-settings:windowsdefender",
            ["antivirus"] = "ms-settings:windowsdefender",
            ["firewall"] = "ms-settings:windowsdefender",
            ["find my device"] = "ms-settings:findmydevice",
            ["device encryption"] = "ms-settings:deviceencryption",
            ["bitlocker"] = "ms-settings:deviceencryption",
            ["location"] = "ms-settings:privacy-location",
            ["camera privacy"] = "ms-settings:privacy-webcam",
            ["microphone privacy"] = "ms-settings:privacy-microphone",
            ["voice activation"] = "ms-settings:privacy-voiceactivation",
            ["notifications privacy"] = "ms-settings:privacy-notifications",
            ["account info privacy"] = "ms-settings:privacy-accountinfo",
            ["contacts privacy"] = "ms-settings:privacy-contacts",
            ["calendar privacy"] = "ms-settings:privacy-calendar",
            ["call history"] = "ms-settings:privacy-callhistory",
            ["email privacy"] = "ms-settings:privacy-email",
            ["messaging privacy"] = "ms-settings:privacy-messaging",
            ["documents privacy"] = "ms-settings:privacy-documents",
            ["pictures privacy"] = "ms-settings:privacy-pictures",
            ["videos privacy"] = "ms-settings:privacy-videos",
            ["file system privacy"] = "ms-settings:privacy-broadfilesystemaccess",
            ["background apps"] = "ms-settings:privacy-backgroundapps",
            ["app diagnostics"] = "ms-settings:privacy-appdiagnostics",
            ["diagnostics"] = "ms-settings:privacy-feedback",
            ["activity history"] = "ms-settings:privacy-activityhistory",
            
            // === WINDOWS UPDATE ===
            ["update"] = "ms-settings:windowsupdate",
            ["windows update"] = "ms-settings:windowsupdate",
            ["updates"] = "ms-settings:windowsupdate",
            ["check for updates"] = "ms-settings:windowsupdate",
            ["update history"] = "ms-settings:windowsupdate-history",
            ["advanced update"] = "ms-settings:windowsupdate-options",
            ["delivery optimization"] = "ms-settings:delivery-optimization",
            ["insider"] = "ms-settings:windowsinsider",
            ["windows insider"] = "ms-settings:windowsinsider",
            
            // === KEYBOARD SETTINGS (THE ONE USER ASKED FOR!) ===
            ["keyboard"] = "ms-settings:easeofaccess-keyboard",
            ["keyboard settings"] = "ms-settings:easeofaccess-keyboard",
            ["input"] = "ms-settings:typing",
            ["typing settings"] = "ms-settings:typing",
        };
        
        #endregion

        #region Control Panel Items
        
        /// <summary>
        /// Control Panel items and their commands
        /// </summary>
        public static readonly Dictionary<string, string> ControlPanelItems = new(StringComparer.OrdinalIgnoreCase)
        {
            ["control panel"] = "control",
            ["add remove programs"] = "appwiz.cpl",
            ["programs and features"] = "appwiz.cpl",
            ["uninstall programs"] = "appwiz.cpl",
            ["device manager"] = "devmgmt.msc",
            ["devices"] = "devmgmt.msc",
            ["drivers"] = "devmgmt.msc",
            ["disk management"] = "diskmgmt.msc",
            ["partition"] = "diskmgmt.msc",
            ["computer management"] = "compmgmt.msc",
            ["services"] = "services.msc",
            ["event viewer"] = "eventvwr.msc",
            ["task scheduler"] = "taskschd.msc",
            ["local security policy"] = "secpol.msc",
            ["group policy"] = "gpedit.msc",
            ["registry editor"] = "regedit",
            ["registry"] = "regedit",
            ["system properties"] = "sysdm.cpl",
            ["environment variables"] = "sysdm.cpl",
            ["network connections"] = "ncpa.cpl",
            ["network adapters"] = "ncpa.cpl",
            ["firewall"] = "firewall.cpl",
            ["windows firewall"] = "firewall.cpl",
            ["power options"] = "powercfg.cpl",
            ["sound control"] = "mmsys.cpl",
            ["sound devices"] = "mmsys.cpl",
            ["playback devices"] = "mmsys.cpl",
            ["recording devices"] = "mmsys.cpl",
            ["mouse properties"] = "main.cpl",
            ["keyboard properties"] = "main.cpl @1",
            ["internet options"] = "inetcpl.cpl",
            ["date time"] = "timedate.cpl",
            ["region format"] = "intl.cpl",
            ["user accounts"] = "netplwiz",
            ["credential manager"] = "control /name Microsoft.CredentialManager",
            ["indexing options"] = "control /name Microsoft.IndexingOptions",
            ["file explorer options"] = "control folders",
            ["folder options"] = "control folders",
        };
        
        #endregion
        
        #region System Commands
        
        /// <summary>
        /// Common Windows system commands
        /// </summary>
        public static readonly Dictionary<string, string> SystemCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            ["task manager"] = "taskmgr",
            ["resource monitor"] = "resmon",
            ["performance monitor"] = "perfmon",
            ["system information"] = "msinfo32",
            ["system config"] = "msconfig",
            ["msconfig"] = "msconfig",
            ["directx diagnostic"] = "dxdiag",
            ["dxdiag"] = "dxdiag",
            ["disk cleanup"] = "cleanmgr",
            ["defragment"] = "dfrgui",
            ["defrag"] = "dfrgui",
            ["character map"] = "charmap",
            ["on-screen keyboard"] = "osk",
            ["osk"] = "osk",
            ["snipping tool"] = "snippingtool",
            ["steps recorder"] = "psr",
            ["problem steps recorder"] = "psr",
            ["remote desktop"] = "mstsc",
            ["rdp"] = "mstsc",
            ["notepad"] = "notepad",
            ["wordpad"] = "wordpad",
            ["paint"] = "mspaint",
            ["calculator"] = "calc",
            ["cmd"] = "cmd",
            ["command prompt"] = "cmd",
            ["powershell"] = "powershell",
            ["terminal"] = "wt",
            ["windows terminal"] = "wt",
            ["file explorer"] = "explorer",
            ["explorer"] = "explorer",
            ["run dialog"] = "explorer shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}",
        };
        
        #endregion
        
        #region Shell Folders
        
        /// <summary>
        /// Special shell folder paths
        /// </summary>
        public static readonly Dictionary<string, string> ShellFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            ["desktop"] = "shell:Desktop",
            ["documents"] = "shell:Personal",
            ["downloads"] = "shell:Downloads",
            ["pictures"] = "shell:My Pictures",
            ["music"] = "shell:My Music",
            ["videos"] = "shell:My Video",
            ["appdata"] = "shell:AppData",
            ["local appdata"] = "shell:Local AppData",
            ["program files"] = "shell:ProgramFiles",
            ["program files x86"] = "shell:ProgramFilesX86",
            ["windows"] = "shell:Windows",
            ["system32"] = "shell:System",
            ["fonts"] = "shell:Fonts",
            ["startup"] = "shell:Startup",
            ["common startup"] = "shell:Common Startup",
            ["recent"] = "shell:Recent",
            ["sendto"] = "shell:SendTo",
            ["templates"] = "shell:Templates",
            ["favorites"] = "shell:Favorites",
            ["quick launch"] = "shell:Quick Launch",
            ["start menu"] = "shell:Start Menu",
            ["common start menu"] = "shell:Common Start Menu",
            ["programs menu"] = "shell:Programs",
            ["recycle bin"] = "shell:RecycleBinFolder",
            ["trash"] = "shell:RecycleBinFolder",
            ["this pc"] = "shell:MyComputerFolder",
            ["my computer"] = "shell:MyComputerFolder",
            ["network"] = "shell:NetworkPlacesFolder",
            ["control panel"] = "shell:ControlPanelFolder",
            ["printers folder"] = "shell:PrintersFolder",
            ["user profile"] = "shell:UserProfiles",
            ["public"] = "shell:Public",
            ["public documents"] = "shell:CommonDocuments",
            ["public pictures"] = "shell:CommonPictures",
            ["public music"] = "shell:CommonMusic",
            ["public videos"] = "shell:CommonVideo",
            ["public downloads"] = "shell:CommonDownloads",
            ["onedrive"] = "shell:OneDrive",
            ["3d objects"] = "shell:3D Objects",
            ["saved games"] = "shell:SavedGames",
            ["contacts"] = "shell:Contacts",
            ["links"] = "shell:Links",
            ["searches"] = "shell:Searches",
            ["camera roll"] = "shell:Camera Roll",
            ["screenshots"] = "shell:Screenshots",
        };
        
        #endregion
        
        #region Lookup Methods
        
        /// <summary>
        /// Find the best matching settings page for a query
        /// </summary>
        public static string? FindSettingsPage(string query)
        {
            var lower = query.ToLower().Trim();
            
            // Direct match
            if (SettingsPages.TryGetValue(lower, out var uri))
                return uri;
            
            // Partial match - find best match
            var matches = SettingsPages
                .Where(kv => lower.Contains(kv.Key) || kv.Key.Contains(lower))
                .OrderByDescending(kv => GetMatchScore(lower, kv.Key))
                .ToList();
            
            if (matches.Count > 0)
                return matches[0].Value;
            
            // Fuzzy match - check for keywords
            foreach (var kv in SettingsPages)
            {
                var keywords = kv.Key.Split(' ', '-', '_');
                if (keywords.Any(k => lower.Contains(k) && k.Length > 3))
                    return kv.Value;
            }
            
            return null;
        }
        
        /// <summary>
        /// Find the best matching control panel item
        /// </summary>
        public static string? FindControlPanelItem(string query)
        {
            var lower = query.ToLower().Trim();
            
            if (ControlPanelItems.TryGetValue(lower, out var cmd))
                return cmd;
            
            var matches = ControlPanelItems
                .Where(kv => lower.Contains(kv.Key) || kv.Key.Contains(lower))
                .OrderByDescending(kv => GetMatchScore(lower, kv.Key))
                .ToList();
            
            return matches.Count > 0 ? matches[0].Value : null;
        }
        
        /// <summary>
        /// Find the best matching system command
        /// </summary>
        public static string? FindSystemCommand(string query)
        {
            var lower = query.ToLower().Trim();
            
            if (SystemCommands.TryGetValue(lower, out var cmd))
                return cmd;
            
            var matches = SystemCommands
                .Where(kv => lower.Contains(kv.Key) || kv.Key.Contains(lower))
                .OrderByDescending(kv => GetMatchScore(lower, kv.Key))
                .ToList();
            
            return matches.Count > 0 ? matches[0].Value : null;
        }
        
        /// <summary>
        /// Find the best matching shell folder
        /// </summary>
        public static string? FindShellFolder(string query)
        {
            var lower = query.ToLower().Trim();
            
            if (ShellFolders.TryGetValue(lower, out var path))
                return path;
            
            var matches = ShellFolders
                .Where(kv => lower.Contains(kv.Key) || kv.Key.Contains(lower))
                .OrderByDescending(kv => GetMatchScore(lower, kv.Key))
                .ToList();
            
            return matches.Count > 0 ? matches[0].Value : null;
        }
        
        /// <summary>
        /// Open a Windows settings page by name
        /// </summary>
        public static bool OpenSettings(string settingName)
        {
            var uri = FindSettingsPage(settingName);
            if (uri == null) return false;
            
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Open a control panel item by name
        /// </summary>
        public static bool OpenControlPanel(string itemName)
        {
            var cmd = FindControlPanelItem(itemName);
            if (cmd == null) return false;
            
            try
            {
                Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Run a system command by name
        /// </summary>
        public static bool RunSystemCommand(string commandName)
        {
            var cmd = FindSystemCommand(commandName);
            if (cmd == null) return false;
            
            try
            {
                Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Open a shell folder by name
        /// </summary>
        public static bool OpenShellFolder(string folderName)
        {
            var path = FindShellFolder(folderName);
            if (path == null) return false;
            
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private static int GetMatchScore(string query, string key)
        {
            if (query == key) return 100;
            if (query.StartsWith(key) || key.StartsWith(query)) return 80;
            if (query.Contains(key)) return 60;
            if (key.Contains(query)) return 40;
            return 0;
        }
        
        #endregion
    }
}
