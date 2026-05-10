using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Comprehensive Windows command reference and execution
    /// Based on Microsoft Windows Commands documentation
    /// </summary>
    public static class WindowsCommands
    {
        /// <summary>
        /// All Windows CMD commands organized by category (A-Z from Microsoft docs)
        /// </summary>
        public static readonly Dictionary<string, List<CommandInfo>> CommandsByCategory = new()
        {
            ["File Management"] = new()
            {
                new("attrib", "Displays or changes file attributes", "attrib +r file.txt"),
                new("copy", "Copies files to another location", "copy file.txt backup.txt"),
                new("del", "Deletes one or more files", "del file.txt"),
                new("dir", "Displays directory contents", "dir /s /b"),
                new("erase", "Deletes one or more files (same as del)", "erase file.txt"),
                new("fc", "Compares two files", "fc file1.txt file2.txt"),
                new("find", "Searches for text in files", "find \"text\" file.txt"),
                new("findstr", "Searches for strings in files (regex)", "findstr /r \"pattern\" file.txt"),
                new("forfiles", "Selects files for batch processing", "forfiles /p C:\\ /s /m *.txt /c \"cmd /c echo @file\""),
                new("move", "Moves files from one location to another", "move file.txt folder\\"),
                new("ren", "Renames a file or directory", "ren oldname.txt newname.txt"),
                new("rename", "Renames a file or directory", "rename oldname.txt newname.txt"),
                new("replace", "Replaces files", "replace source.txt dest\\"),
                new("robocopy", "Robust file copy utility", "robocopy source dest /mir"),
                new("tree", "Displays directory structure graphically", "tree /f"),
                new("type", "Displays contents of a text file", "type file.txt"),
                new("xcopy", "Copies files and directory trees", "xcopy source dest /s /e"),
                new("mklink", "Creates symbolic links", "mklink link target"),
                new("append", "Allows programs to open files in specified directories", "append path"),
                new("comp", "Compares contents of two files byte by byte", "comp file1.txt file2.txt"),
                new("compact", "Displays or alters compression of files on NTFS", "compact /c file.txt"),
                new("expand", "Expands one or more compressed files", "expand archive.cab -F:* dest"),
                new("extract", "Extracts files from a cabinet", "extract cabinet.cab"),
                new("makecab", "Packages files into a cabinet (.cab) archive", "makecab file.txt archive.cab"),
            },
            ["Directory Management"] = new()
            {
                new("cd", "Changes the current directory", "cd \\Users\\Public"),
                new("chdir", "Changes the current directory", "chdir \\Users"),
                new("md", "Creates a directory", "md newfolder"),
                new("mkdir", "Creates a directory", "mkdir newfolder"),
                new("rd", "Removes a directory", "rd emptyfolder"),
                new("rmdir", "Removes a directory", "rmdir /s /q folder"),
                new("pushd", "Saves current directory and changes to new one", "pushd \\temp"),
                new("popd", "Returns to directory saved by pushd", "popd"),
            },
            ["Disk Management"] = new()
            {
                new("active", "Marks disk partition as active (diskpart)", "active"),
                new("add", "Adds mirror to simple volume (diskpart)", "add disk=n"),
                new("add alias", "Adds aliases to alias environment", "add alias name=value"),
                new("add volume", "Adds volume to shadow copy set", "add volume c:"),
                new("assign", "Assigns drive letter to volume (diskpart)", "assign letter=E"),
                new("attach-vdisk", "Attaches virtual hard disk", "attach vdisk"),
                new("attributes", "Displays/sets disk attributes (diskpart)", "attributes disk"),
                new("automount", "Enables/disables automount feature", "automount enable"),
                new("break", "Breaks mirrored volume (diskpart)", "break disk=n"),
                new("chkdsk", "Checks disk and displays status report", "chkdsk C: /f"),
                new("chkntfs", "Displays or modifies automatic disk checking", "chkntfs /d"),
                new("clean", "Removes all partitions from disk (diskpart)", "clean"),
                new("compact", "Displays or alters compression on NTFS", "compact /c file.txt"),
                new("compact-vdisk", "Compacts virtual hard disk", "compact vdisk"),
                new("convert", "Converts FAT volumes to NTFS", "convert D: /fs:ntfs"),
                new("create", "Creates partition/volume/vdisk (diskpart)", "create partition primary"),
                new("defrag", "Defragments hard drives", "defrag C: /o"),
                new("delete", "Deletes partition/volume (diskpart)", "delete partition"),
                new("detach-vdisk", "Detaches virtual hard disk", "detach vdisk"),
                new("detail", "Shows details about disk/partition (diskpart)", "detail disk"),
                new("diskpart", "Disk partitioning utility", "diskpart"),
                new("diskperf", "Enables/disables disk performance counters", "diskperf -y"),
                new("diskshadow", "Shadow copy management", "diskshadow"),
                new("expand-vdisk", "Expands virtual hard disk", "expand vdisk maximum=20000"),
                new("extend", "Extends volume (diskpart)", "extend size=1000"),
                new("filesystems", "Displays file system info (diskpart)", "filesystems"),
                new("format", "Formats a disk", "format D: /fs:ntfs /q"),
                new("fsutil", "File system utility", "fsutil volume diskfree C:"),
                new("gpt", "Assigns GPT attributes (diskpart)", "gpt attributes=0x0000000000000001"),
                new("import", "Imports disk group (diskpart)", "import"),
                new("label", "Creates/changes/deletes volume label", "label D: MyDrive"),
                new("list", "Lists disks/partitions/volumes (diskpart)", "list disk"),
                new("merge-vdisk", "Merges differencing VHD", "merge vdisk depth=1"),
                new("mountvol", "Creates/deletes/lists volume mount points", "mountvol"),
                new("offline", "Takes disk/volume offline (diskpart)", "offline disk"),
                new("online", "Brings disk/volume online (diskpart)", "online disk"),
                new("recover", "Refreshes disk state (diskpart)", "recover"),
                new("remove", "Removes drive letter (diskpart)", "remove letter=E"),
                new("repair", "Repairs RAID-5 volume (diskpart)", "repair disk=n"),
                new("rescan", "Rescans for new disks (diskpart)", "rescan"),
                new("retain", "Prepares volume for boot (diskpart)", "retain"),
                new("san", "Displays/sets SAN policy (diskpart)", "san"),
                new("select", "Selects disk/partition/volume (diskpart)", "select disk 0"),
                new("setid", "Changes partition type (diskpart)", "setid id=07"),
                new("shrink", "Shrinks volume (diskpart)", "shrink desired=1000"),
                new("uniqueid", "Displays/sets GPT identifier (diskpart)", "uniqueid disk"),
                new("vol", "Displays disk volume label and serial number", "vol C:"),
            },
            ["System Information"] = new()
            {
                new("date", "Displays or sets the date", "date /t"),
                new("hostname", "Displays computer name", "hostname"),
                new("systeminfo", "Displays detailed system configuration", "systeminfo"),
                new("time", "Displays or sets the system time", "time /t"),
                new("ver", "Displays Windows version", "ver"),
                new("whoami", "Displays current user information", "whoami /all"),
                new("wmic", "Windows Management Instrumentation", "wmic cpu get name"),
                new("driverquery", "Lists installed device drivers", "driverquery /v"),
                new("gpresult", "Displays Group Policy information", "gpresult /r"),
                new("msinfo32", "System Information GUI", "msinfo32"),
                new("winver", "Displays Windows version dialog", "winver"),
                new("wevtutil", "Windows Events command-line utility", "wevtutil qe System /c:5"),
                new("eventcreate", "Creates custom event in event log", "eventcreate /t information /id 100 /l application /d \"Test\""),
                new("logman", "Manages Performance Monitor data collector sets", "logman query"),
                new("typeperf", "Writes performance counter data to window or log", "typeperf \"\\Processor(_Total)\\% Processor Time\""),
                new("perfmon", "Performance Monitor", "perfmon /res"),
                new("resmon", "Resource Monitor", "resmon"),
                new("taskmgr", "Task Manager", "taskmgr"),
                new("msconfig", "System Configuration utility", "msconfig"),
                new("mmc", "Microsoft Management Console", "mmc"),
            },
            ["Process Management"] = new()
            {
                new("start", "Starts a program or command", "start notepad"),
                new("tasklist", "Lists running processes", "tasklist /v"),
                new("taskkill", "Terminates processes", "taskkill /im notepad.exe /f"),
                new("schtasks", "Schedules commands and programs", "schtasks /query"),
                new("at", "Schedules commands (deprecated)", "at 12:00 cmd"),
                new("shutdown", "Shuts down or restarts computer", "shutdown /s /t 60"),
                new("logoff", "Logs off current user", "logoff"),
                new("tskill", "Terminates a process", "tskill processid"),
                new("psr", "Problem Steps Recorder", "psr /start /output steps.zip"),
                new("query", "Displays information about processes, sessions, and servers", "query process"),
                new("qprocess", "Displays information about processes", "qprocess *"),
                new("quser", "Displays information about user sessions", "quser"),
                new("qwinsta", "Displays information about sessions", "qwinsta"),
                new("rwinsta", "Resets session subsystem hardware and software", "rwinsta sessionid"),
                new("tsdiscon", "Disconnects a session from terminal server", "tsdiscon sessionid"),
                new("tscon", "Connects to another session", "tscon sessionid"),
                new("msg", "Sends a message to a user", "msg username \"Hello\""),
                new("sc", "Service Control - manages services", "sc query"),
                new("sc config", "Modifies service configuration", "sc config servicename start=auto"),
                new("sc create", "Creates a service entry", "sc create myservice binpath=path"),
                new("sc delete", "Deletes a service", "sc delete servicename"),
                new("sc start", "Starts a service", "sc start servicename"),
                new("sc stop", "Stops a service", "sc stop servicename"),
                new("sc query", "Queries service status", "sc query servicename"),
                new("sc qc", "Queries service configuration", "sc qc servicename"),
            },
            ["Network Commands"] = new()
            {
                new("arp", "Displays and modifies ARP cache", "arp -a"),
                new("bitsadmin", "Background Intelligent Transfer Service", "bitsadmin /list"),
                new("dnscmd", "DNS server management", "dnscmd /info"),
                new("finger", "Displays user information on remote system", "finger user@host"),
                new("ftp", "FTP client", "ftp ftp.example.com"),
                new("getmac", "Displays MAC addresses", "getmac /v"),
                new("hostname", "Displays computer name", "hostname"),
                new("ipconfig", "Displays IP configuration", "ipconfig /all"),
                new("ipxroute", "Displays/modifies IPX routing table", "ipxroute config"),
                new("irftp", "Sends files over infrared link", "irftp file.txt"),
                new("jetpack", "Compacts WINS or DHCP database", "jetpack wins.mdb temp.mdb"),
                new("mrinfo", "Displays multicast router info", "mrinfo router"),
                new("nbtstat", "Displays NetBIOS statistics", "nbtstat -n"),
                new("net", "Network commands", "net user"),
                new("net accounts", "Sets password and logon requirements", "net accounts"),
                new("net computer", "Adds/removes computers from domain", "net computer \\\\pc /add"),
                new("net config", "Displays workstation/server config", "net config workstation"),
                new("net continue", "Continues paused service", "net continue spooler"),
                new("net file", "Displays open shared files", "net file"),
                new("net group", "Manages global groups", "net group"),
                new("net help", "Displays help for net commands", "net help user"),
                new("net helpmsg", "Explains Windows error messages", "net helpmsg 3534"),
                new("net localgroup", "Manages local groups", "net localgroup administrators"),
                new("net name", "Adds/deletes messaging name", "net name"),
                new("net pause", "Pauses a service", "net pause spooler"),
                new("net print", "Displays print jobs", "net print \\\\server\\printer"),
                new("net send", "Sends messages (deprecated)", "net send * message"),
                new("net session", "Lists/disconnects sessions", "net session"),
                new("net share", "Manages shared resources", "net share"),
                new("net start", "Starts a service", "net start spooler"),
                new("net statistics", "Displays workstation/server stats", "net statistics workstation"),
                new("net stop", "Stops a service", "net stop spooler"),
                new("net time", "Synchronizes time", "net time \\\\server"),
                new("net use", "Connects to shared resources", "net use Z: \\\\server\\share"),
                new("net user", "Manages user accounts", "net user username /add"),
                new("net view", "Displays shared resources", "net view \\\\server"),
                new("netcfg", "Network configuration", "netcfg -l"),
                new("netsh", "Network shell utility", "netsh wlan show profiles"),
                new("netstat", "Displays network statistics", "netstat -an"),
                new("nfsadmin", "NFS administration", "nfsadmin server"),
                new("nfsshare", "Controls NFS shares", "nfsshare"),
                new("nfsstat", "Displays NFS statistics", "nfsstat"),
                new("nltest", "Network diagnostics", "nltest /dclist:domain"),
                new("nslookup", "DNS lookup utility", "nslookup google.com"),
                new("ntfrsutl", "NTFRS utility", "ntfrsutl version"),
                new("pathping", "Traces route with latency info", "pathping google.com"),
                new("ping", "Tests network connectivity", "ping google.com"),
                new("pktmon", "Packet monitor", "pktmon start"),
                new("portqry", "Port query utility", "portqry -n server -e 80"),
                new("rcp", "Remote copy (deprecated)", "rcp file host:file"),
                new("rdpsign", "Signs RDP files", "rdpsign file.rdp"),
                new("rexec", "Remote execution (deprecated)", "rexec host command"),
                new("route", "Displays/modifies routing table", "route print"),
                new("rpcinfo", "RPC information", "rpcinfo -p"),
                new("rpcping", "Pings RPC server", "rpcping -s server"),
                new("rsh", "Remote shell (deprecated)", "rsh host command"),
                new("telnet", "Telnet client", "telnet host 80"),
                new("tftp", "TFTP client", "tftp -i host get file"),
                new("tracert", "Traces route to destination", "tracert google.com"),
                new("waitfor", "Sends/waits for signal", "waitfor signal"),
                new("winrs", "Windows Remote Shell", "winrs -r:server cmd"),
                new("wmic", "Windows Management Instrumentation", "wmic cpu get name"),
            },
            ["User Management"] = new()
            {
                new("net user", "Manages user accounts", "net user username /add"),
                new("net localgroup", "Manages local groups", "net localgroup administrators"),
                new("runas", "Runs program as different user", "runas /user:admin cmd"),
                new("cmdkey", "Manages stored credentials", "cmdkey /list"),
                new("whoami", "Displays current user and group info", "whoami /groups"),
                new("quser", "Displays logged on users", "quser"),
                new("query user", "Displays user session information", "query user"),
                new("lusrmgr.msc", "Local Users and Groups Manager", "lusrmgr.msc"),
                new("control userpasswords2", "User Accounts dialog", "control userpasswords2"),
                new("netplwiz", "User Accounts wizard", "netplwiz"),
            },
            ["Security"] = new()
            {
                new("cipher", "Displays/alters encryption", "cipher /e folder"),
                new("icacls", "Displays/modifies file permissions", "icacls file.txt"),
                new("cacls", "Displays/modifies ACLs (deprecated)", "cacls file.txt"),
                new("takeown", "Takes ownership of files", "takeown /f file.txt"),
                new("auditpol", "Displays/modifies audit policies", "auditpol /get /category:*"),
                new("secedit", "Configures and analyzes system security", "secedit /analyze /db secedit.sdb"),
                new("certutil", "Certificate utility", "certutil -hashfile file.txt SHA256"),
                new("certreq", "Certificate request utility", "certreq -new request.inf request.req"),
                new("wbadmin", "Windows Backup administration", "wbadmin get versions"),
                new("manage-bde", "BitLocker Drive Encryption management", "manage-bde -status"),
                new("bdehdcfg", "BitLocker Drive Encryption HD configuration", "bdehdcfg -target default"),
                new("repair-bde", "Repairs BitLocker encrypted volumes", "repair-bde C: D: -rp password"),
                new("secpol.msc", "Local Security Policy", "secpol.msc"),
                new("gpedit.msc", "Group Policy Editor", "gpedit.msc"),
                new("gpupdate", "Updates Group Policy settings", "gpupdate /force"),
                new("gpresult", "Displays Resultant Set of Policy", "gpresult /r"),
                new("setx", "Sets environment variables permanently", "setx PATH \"%PATH%;C:\\newpath\""),
                new("syskey", "Secures Windows account database (deprecated)", "syskey"),
            },
            ["Batch/Scripting"] = new()
            {
                new("call", "Calls another batch file", "call script.bat"),
                new("choice", "Prompts user to make a choice", "choice /c YN /m \"Continue?\""),
                new("cls", "Clears the screen", "cls"),
                new("cmd", "Starts new command shell", "cmd /c dir"),
                new("color", "Sets console colors", "color 0a"),
                new("echo", "Displays messages or toggles echo", "echo Hello World"),
                new("endlocal", "Ends localization of environment", "endlocal"),
                new("exit", "Exits command shell", "exit /b 0"),
                new("for", "Runs command for each item", "for %i in (*.txt) do echo %i"),
                new("goto", "Directs to labeled line", "goto :label"),
                new("if", "Conditional processing", "if exist file.txt echo Found"),
                new("pause", "Suspends processing", "pause"),
                new("prompt", "Changes command prompt", "prompt $p$g"),
                new("rem", "Records comments in batch file", "rem This is a comment"),
                new("set", "Displays/sets environment variables", "set PATH"),
                new("setlocal", "Begins localization of environment", "setlocal enabledelayedexpansion"),
                new("shift", "Shifts batch parameters", "shift"),
                new("title", "Sets window title", "title My Window"),
                new("timeout", "Waits for specified time", "timeout /t 5"),
            },
            ["Recovery/Repair"] = new()
            {
                new("bcdedit", "Boot configuration editor", "bcdedit /enum"),
                new("bootrec", "Boot recovery tool", "bootrec /fixmbr"),
                new("reagentc", "Windows Recovery Environment", "reagentc /info"),
                new("sfc", "System File Checker", "sfc /scannow"),
                new("dism", "Deployment Image Servicing", "dism /online /cleanup-image /restorehealth"),
                new("bcdboot", "Boot configuration data boot file creation", "bcdboot C:\\Windows"),
                new("bootcfg", "Configures boot.ini (legacy)", "bootcfg /query"),
                new("chkdsk", "Checks disk for errors", "chkdsk C: /f /r"),
                new("cleanmgr", "Disk Cleanup utility", "cleanmgr /d C:"),
                new("recimg", "Creates custom recovery image", "recimg /createimage D:\\backup"),
                new("rstrui", "System Restore", "rstrui"),
                new("wbadmin", "Windows Backup administration", "wbadmin start backup"),
                new("wbadmin start recovery", "Starts recovery operation", "wbadmin start recovery"),
                new("wbadmin start systemstatebackup", "Backs up system state", "wbadmin start systemstatebackup -backuptarget:D:"),
                new("wbadmin start systemstaterecovery", "Recovers system state", "wbadmin start systemstaterecovery"),
            },
            ["Power Management"] = new()
            {
                new("powercfg", "Power configuration utility", "powercfg /batteryreport"),
                new("powercfg /a", "Reports sleep states available", "powercfg /a"),
                new("powercfg /energy", "Generates energy efficiency report", "powercfg /energy"),
                new("powercfg /batteryreport", "Generates battery usage report", "powercfg /batteryreport"),
                new("powercfg /sleepstudy", "Generates sleep study report", "powercfg /sleepstudy"),
                new("powercfg /devicequery", "Lists devices with power capabilities", "powercfg /devicequery wake_armed"),
                new("powercfg /lastwake", "Reports what woke the system", "powercfg /lastwake"),
                new("powercfg /waketimers", "Lists active wake timers", "powercfg /waketimers"),
                new("powercfg /requests", "Lists power requests", "powercfg /requests"),
                new("powercfg /setactive", "Sets active power scheme", "powercfg /setactive SCHEME_BALANCED"),
                new("powercfg /hibernate", "Enables/disables hibernation", "powercfg /hibernate on"),
                new("shutdown", "Shuts down, restarts, or hibernates", "shutdown /h"),
                new("rundll32 powrprof.dll,SetSuspendState", "Puts computer to sleep", "rundll32 powrprof.dll,SetSuspendState 0,1,0"),
            },
            ["Printing"] = new()
            {
                new("print", "Prints a text file", "print file.txt"),
                new("printui", "Printer user interface", "printui /s /t2"),
                new("prncnfg", "Configures or displays printer configuration", "prncnfg -g -p \"PrinterName\""),
                new("prndrvr", "Adds, deletes, and lists printer drivers", "prndrvr -l"),
                new("prnjobs", "Pauses, resumes, cancels, and lists print jobs", "prnjobs -l -p \"PrinterName\""),
                new("prnmngr", "Adds, deletes, and lists printers", "prnmngr -l"),
                new("prnport", "Creates, deletes, and lists TCP/IP printer ports", "prnport -l"),
                new("prnqctl", "Prints a test page, pauses or resumes a printer", "prnqctl -e -p \"PrinterName\""),
                new("lpq", "Displays status of print queue on LPD server", "lpq -S server -P printer"),
                new("lpr", "Sends file to LPD server for printing", "lpr -S server -P printer file.txt"),
            },
            ["Other Utilities"] = new()
            {
                new("assoc", "Displays/modifies file associations", "assoc .txt"),
                new("clip", "Copies output to clipboard", "dir | clip"),
                new("comp", "Compares contents of two files", "comp file1 file2"),
                new("doskey", "Edits command lines and creates macros", "doskey /history"),
                new("expand", "Expands compressed files", "expand file.cab"),
                new("ftype", "Displays/modifies file type associations", "ftype txtfile"),
                new("help", "Provides help for commands", "help dir"),
                new("mode", "Configures system devices", "mode con cols=120 lines=50"),
                new("more", "Displays output one screen at a time", "type file.txt | more"),
                new("openfiles", "Displays files opened by remote users", "openfiles /query"),
                new("path", "Displays/sets search path", "path"),
                new("recover", "Recovers readable info from bad disk", "recover file.txt"),
                new("reg", "Registry command-line tool", "reg query HKLM\\SOFTWARE"),
                new("regsvr32", "Registers/unregisters DLLs", "regsvr32 file.dll"),
                new("sort", "Sorts input", "sort file.txt"),
                new("subst", "Associates path with drive letter", "subst X: C:\\folder"),
                new("where", "Locates files matching pattern", "where notepad"),
                new("wusa", "Windows Update Standalone Installer", "wusa update.msu /quiet"),
                new("msiexec", "Windows Installer", "msiexec /i package.msi"),
                new("mstsc", "Remote Desktop Connection", "mstsc /v:server"),
                new("control", "Opens Control Panel items", "control printers"),
                new("explorer", "Opens File Explorer", "explorer C:\\"),
                new("notepad", "Opens Notepad", "notepad file.txt"),
                new("calc", "Opens Calculator", "calc"),
                new("mspaint", "Opens Paint", "mspaint"),
                new("write", "Opens WordPad", "write"),
                new("charmap", "Character Map", "charmap"),
                new("magnify", "Magnifier", "magnify"),
                new("narrator", "Narrator accessibility tool", "narrator"),
                new("osk", "On-Screen Keyboard", "osk"),
                new("snippingtool", "Snipping Tool", "snippingtool"),
                new("soundrecorder", "Sound Recorder", "soundrecorder"),
                new("wmplayer", "Windows Media Player", "wmplayer"),
                new("iexplore", "Internet Explorer", "iexplore"),
                new("msedge", "Microsoft Edge", "msedge"),
                new("winword", "Microsoft Word", "winword"),
                new("excel", "Microsoft Excel", "excel"),
                new("powerpnt", "Microsoft PowerPoint", "powerpnt"),
                new("outlook", "Microsoft Outlook", "outlook"),
                new("devmgmt.msc", "Device Manager", "devmgmt.msc"),
                new("diskmgmt.msc", "Disk Management", "diskmgmt.msc"),
                new("compmgmt.msc", "Computer Management", "compmgmt.msc"),
                new("services.msc", "Services", "services.msc"),
                new("eventvwr.msc", "Event Viewer", "eventvwr.msc"),
                new("taskschd.msc", "Task Scheduler", "taskschd.msc"),
                new("fsmgmt.msc", "Shared Folders", "fsmgmt.msc"),
                new("certmgr.msc", "Certificate Manager", "certmgr.msc"),
                new("hdwwiz.cpl", "Add Hardware Wizard", "hdwwiz.cpl"),
                new("appwiz.cpl", "Programs and Features", "appwiz.cpl"),
                new("desk.cpl", "Display Settings", "desk.cpl"),
                new("firewall.cpl", "Windows Firewall", "firewall.cpl"),
                new("inetcpl.cpl", "Internet Options", "inetcpl.cpl"),
                new("intl.cpl", "Regional Settings", "intl.cpl"),
                new("joy.cpl", "Game Controllers", "joy.cpl"),
                new("main.cpl", "Mouse Properties", "main.cpl"),
                new("mmsys.cpl", "Sound Settings", "mmsys.cpl"),
                new("ncpa.cpl", "Network Connections", "ncpa.cpl"),
                new("powercfg.cpl", "Power Options", "powercfg.cpl"),
                new("sysdm.cpl", "System Properties", "sysdm.cpl"),
                new("timedate.cpl", "Date and Time", "timedate.cpl"),
                new("wscui.cpl", "Security and Maintenance", "wscui.cpl"),
                new("optionalfeatures", "Windows Features", "optionalfeatures"),
                new("winget", "Windows Package Manager", "winget search notepad"),
                new("winget install", "Installs a package", "winget install Microsoft.VisualStudioCode"),
                new("winget upgrade", "Upgrades packages", "winget upgrade --all"),
                new("winget list", "Lists installed packages", "winget list"),
                new("winget uninstall", "Uninstalls a package", "winget uninstall PackageName"),
                new("wsl", "Windows Subsystem for Linux", "wsl --list"),
                new("wsl --install", "Installs WSL", "wsl --install"),
                new("wsl --update", "Updates WSL", "wsl --update"),
            },
            ["Active Directory"] = new()
            {
                new("dsadd", "Adds objects to Active Directory", "dsadd user \"CN=User,DC=domain,DC=com\""),
                new("dsget", "Displays properties of AD objects", "dsget user \"CN=User,DC=domain,DC=com\""),
                new("dsmod", "Modifies AD objects", "dsmod user \"CN=User,DC=domain,DC=com\" -pwd newpass"),
                new("dsmove", "Moves AD objects", "dsmove \"CN=User,DC=domain,DC=com\" -newparent \"OU=Users,DC=domain,DC=com\""),
                new("dsquery", "Queries Active Directory", "dsquery user -name *admin*"),
                new("dsrm", "Removes AD objects", "dsrm \"CN=User,DC=domain,DC=com\""),
                new("csvde", "Imports/exports AD data using CSV", "csvde -f export.csv"),
                new("ldifde", "Imports/exports AD data using LDIF", "ldifde -f export.ldf"),
                new("ntdsutil", "AD database maintenance", "ntdsutil"),
                new("dcdiag", "Domain Controller diagnostics", "dcdiag /v"),
                new("repadmin", "AD replication diagnostics", "repadmin /showrepl"),
                new("nltest", "Network logon test", "nltest /dclist:domain"),
                new("setspn", "Manages Service Principal Names", "setspn -L computername"),
                new("klist", "Lists Kerberos tickets", "klist"),
                new("ksetup", "Configures Kerberos realm", "ksetup"),
                new("adprep", "Prepares AD for upgrades", "adprep /forestprep"),
            },
            ["Hyper-V"] = new()
            {
                new("virtmgmt.msc", "Hyper-V Manager", "virtmgmt.msc"),
                new("vmconnect", "Virtual Machine Connection", "vmconnect localhost VMName"),
                new("Get-VM", "Lists virtual machines (PowerShell)", "powershell Get-VM"),
                new("Start-VM", "Starts a virtual machine (PowerShell)", "powershell Start-VM -Name VMName"),
                new("Stop-VM", "Stops a virtual machine (PowerShell)", "powershell Stop-VM -Name VMName"),
                new("Checkpoint-VM", "Creates VM checkpoint (PowerShell)", "powershell Checkpoint-VM -Name VMName"),
                new("Export-VM", "Exports a virtual machine (PowerShell)", "powershell Export-VM -Name VMName -Path D:\\Export"),
            },
            ["Windows Defender"] = new()
            {
                new("MpCmdRun", "Windows Defender command-line", "MpCmdRun -Scan -ScanType 1"),
                new("MpCmdRun -Scan", "Runs antivirus scan", "MpCmdRun -Scan -ScanType 2"),
                new("MpCmdRun -SignatureUpdate", "Updates virus definitions", "MpCmdRun -SignatureUpdate"),
                new("MpCmdRun -Restore", "Restores quarantined items", "MpCmdRun -Restore -All"),
                new("MpCmdRun -GetFiles", "Collects support files", "MpCmdRun -GetFiles"),
                new("windowsdefender:", "Opens Windows Security", "start windowsdefender:"),
            },
            ["Remote Desktop Services"] = new()
            {
                new("mstsc", "Remote Desktop Connection", "mstsc /v:server /f"),
                new("mstsc /admin", "Connects to admin session", "mstsc /v:server /admin"),
                new("mstsc /span", "Spans multiple monitors", "mstsc /v:server /span"),
                new("mstsc /multimon", "Uses all monitors", "mstsc /v:server /multimon"),
                new("qwinsta", "Displays session information", "qwinsta /server:servername"),
                new("rwinsta", "Resets a session", "rwinsta sessionid /server:servername"),
                new("shadow", "Monitors another session", "shadow sessionid /server:servername"),
                new("tscon", "Connects to a session", "tscon sessionid /dest:sessionname"),
                new("tsdiscon", "Disconnects a session", "tsdiscon sessionid /server:servername"),
                new("change logon", "Enables/disables session logons", "change logon /query"),
                new("change port", "Lists/changes COM port mappings", "change port /query"),
                new("change user", "Changes install mode", "change user /query"),
            },
        };

        /// <summary>
        /// Quick lookup of all commands
        /// </summary>
        public static readonly Dictionary<string, CommandInfo> AllCommands = BuildAllCommands();

        private static Dictionary<string, CommandInfo> BuildAllCommands()
        {
            var all = new Dictionary<string, CommandInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in CommandsByCategory.Values)
            {
                foreach (var cmd in category)
                {
                    all[cmd.Name] = cmd;
                }
            }
            return all;
        }

        /// <summary>
        /// Execute a Windows command and return the output
        /// </summary>
        public static async Task<string> ExecuteAsync(string command, int timeoutMs = 30000)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return "❌ Failed to start command";

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMs))
                {
                    process.Kill();
                    return "❌ Command timed out";
                }

                var output = await outputTask;
                var error = await errorTask;

                if (!string.IsNullOrEmpty(error) && string.IsNullOrEmpty(output))
                    return $"❌ Error: {error}";

                return string.IsNullOrEmpty(output) ? "✓ Command completed (no output)" : output;
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Get help for a specific command
        /// </summary>
        public static string GetHelp(string commandName)
        {
            if (AllCommands.TryGetValue(commandName, out var cmd))
            {
                return $"📘 **{cmd.Name}**\n\n" +
                       $"{cmd.Description}\n\n" +
                       $"Example: `{cmd.Example}`\n\n" +
                       $"💡 Run `{cmd.Name} /?` for full help";
            }
            return $"❓ Unknown command: {commandName}. Try 'list commands' to see all available commands.";
        }

        /// <summary>
        /// List all commands or commands in a category
        /// </summary>
        public static string ListCommands(string? category = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📋 **Windows Commands Reference**\n");

            if (string.IsNullOrEmpty(category))
            {
                // List categories
                sb.AppendLine("Categories:");
                foreach (var cat in CommandsByCategory.Keys)
                {
                    sb.AppendLine($"  • {cat} ({CommandsByCategory[cat].Count} commands)");
                }
                sb.AppendLine("\nSay 'list [category] commands' for details, or 'help [command]' for specific help.");
            }
            else
            {
                // Find matching category
                foreach (var kvp in CommandsByCategory)
                {
                    if (kvp.Key.Contains(category, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"**{kvp.Key}:**\n");
                        foreach (var cmd in kvp.Value)
                        {
                            sb.AppendLine($"  `{cmd.Name}` - {cmd.Description}");
                        }
                        return sb.ToString();
                    }
                }
                sb.AppendLine($"Category '{category}' not found. Available: {string.Join(", ", CommandsByCategory.Keys)}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Try to handle a command request
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant().Trim();

            // List commands
            if (lower == "list commands" || lower == "show commands" || lower == "windows commands")
            {
                return ListCommands();
            }

            // List category commands
            if (lower.StartsWith("list ") && lower.EndsWith(" commands"))
            {
                var category = lower.Replace("list ", "").Replace(" commands", "").Trim();
                return ListCommands(category);
            }

            // Help for specific command
            if (lower.StartsWith("help ") || lower.StartsWith("what is ") || lower.StartsWith("explain "))
            {
                var cmdName = lower.Replace("help ", "").Replace("what is ", "").Replace("explain ", "").Trim();
                if (AllCommands.ContainsKey(cmdName))
                {
                    return GetHelp(cmdName);
                }
            }

            // Direct command execution (if it starts with a known command)
            var firstWord = lower.Split(' ')[0];
            if (AllCommands.ContainsKey(firstWord))
            {
                // Execute the command
                return await ExecuteAsync(input);
            }

            return null;
        }
    }

    public class CommandInfo
    {
        public string Name { get; }
        public string Description { get; }
        public string Example { get; }

        public CommandInfo(string name, string description, string example)
        {
            Name = name;
            Description = description;
            Example = example;
        }
    }
}
