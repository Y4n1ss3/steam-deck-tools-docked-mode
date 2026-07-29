using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using CommonHelpers;
using ExternalHelpers;
using PowerControl.Helper;
using PowerControl.Menu;

namespace PowerControl
{
    public class ProfilesController : IDisposable
    {
        public const bool AutoCreateProfiles = false;
        public const int ApplyProfileDelayMs = 500;
        public const int ResetProfileDelayMs = 500;

        private Dictionary<int, PowerControl.Helper.ProfileSettings> watchedProcesses = new Dictionary<int, PowerControl.Helper.ProfileSettings>();
        private HashSet<int> manuallyDetectedProcesses = new HashSet<int>(); // Track processes detected manually (not by RTSS)
        private DateTime lastManualChromeCheck = DateTime.MinValue; // Throttle manual Chrome detection
        private Dictionary<MenuItemWithOptions, String>? changedSettings;
        private CancellationTokenSource? changeTask;

        private System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer()
        {
            Interval = 1000
        };

        public IEnumerable<String> WatchedProfiles
        {
            get
            {
                foreach (var process in watchedProcesses)
                    yield return process.Value.ProfileName;
            }
        }

        public ProfileSettings? CurrentProfileSettings { get; private set; }
        public ProfileSettings AutostartProfileSettings { get; private set; }

        public ProfilesController()
        {
            PowerControl.Options.Profiles.Controller = this;
            MenuStack.Root.ValueChanged += Root_OnOptionValueChange;

            timer.Start();
            timer.Tick += Timer_Tick;

            ApplyAutostartProfile();
        }

        public void ApplyAutostartProfile() {
            if (DisplayConfig.IsExternalConnected.GetValueOrDefault(false))
                AutostartProfileSettings = new ProfileSettings("PowerControl", "Autostart.Docked");
            else
                AutostartProfileSettings = new ProfileSettings("PowerControl", "Autostart");
                
            ProfileChanged(null);
            ApplyProfile(AutostartProfileSettings);
        }

        ~ProfilesController()
        {
            Dispose();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            PowerControl.Options.Profiles.Controller = null;
            MenuStack.Root.ValueChanged -= Root_OnOptionValueChange;
            timer.Stop();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            timer.Enabled = false;

            try { RefreshProfiles(); }
            finally { timer.Enabled = true; }
        }

        private void RefreshProfiles()
        {
            // DEBUG: Log every refresh to see if method is called
            // Log.TraceLine("ProfilesController: RefreshProfiles called");
            
            if (CurrentProfileSettings != null && (DisplayConfig.IsExternalConnected.GetValueOrDefault(false) && !CurrentProfileSettings.ConfigFile.Contains(".Docked") || !DisplayConfig.IsExternalConnected.GetValueOrDefault(false) && CurrentProfileSettings.ConfigFile.Contains(".Docked")))
            {
                foreach (var process in watchedProcesses)
                    RemoveProcess(process.Key);
            }

            OSDHelpers.Applications.Instance.Refresh();

            if (OSDHelpers.Applications.Instance.FindForeground(out var processId, out var processName))
            {
                if (!BringUpProcess(processId))
                    AddProcess(processId, processName);
            }
            else
            {
                // RTSS didn't find foreground app - try to detect Chrome manually
                // Only log once per second to avoid spam
                TryDetectChromeManually();
            }

            foreach (var process in watchedProcesses)
            {
                // Don't remove manually detected processes (they're not in RTSS)
                if (manuallyDetectedProcesses.Contains(process.Key))
                {
                    // Check if process is still running
                    try
                    {
                        var proc = Process.GetProcessById(process.Key);
                        if (proc.HasExited)
                        {
                            Log.TraceLine("ProfilesController: Manually detected process {0} has exited", process.Key);
                            manuallyDetectedProcesses.Remove(process.Key);
                            RemoveProcess(process.Key);
                        }
                    }
                    catch
                    {
                        // Process no longer exists
                        Log.TraceLine("ProfilesController: Manually detected process {0} no longer exists", process.Key);
                        manuallyDetectedProcesses.Remove(process.Key);
                        RemoveProcess(process.Key);
                    }
                    continue;
                }
                
                // For RTSS-detected processes, check if they're still running in RTSS
                if (OSDHelpers.Applications.Instance.IsRunning(process.Key))
                    continue;
                RemoveProcess(process.Key);
            }
        }

        private void TryDetectChromeManually()
        {
            // Throttle detection to once per second
            var now = DateTime.Now;
            if ((now - lastManualChromeCheck).TotalSeconds < 1.0)
                return;
            
            lastManualChromeCheck = now;

            try
            {
                // Only detect foreground Chrome (don't detect background processes)
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero)
                    return;

                uint foregroundProcessId;
                GetWindowThreadProcessId(foregroundWindow, out foregroundProcessId);
                
                if (foregroundProcessId == 0)
                    return;

                try
                {
                    var process = Process.GetProcessById((int)foregroundProcessId);
                    if (process.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase))
                    {
                        // Log.TraceLine("ProfilesController: Manual Chrome detection - Foreground PID: {0}", foregroundProcessId);
                        
                        if (!watchedProcesses.ContainsKey((int)foregroundProcessId))
                        {
                            Log.TraceLine("ProfilesController: Adding Chrome process manually (not in RTSS)");
                            manuallyDetectedProcesses.Add((int)foregroundProcessId);
                            AddProcess((int)foregroundProcessId, "chrome");
                        }
                        else
                        {
                            BringUpProcess((int)foregroundProcessId);
                        }
                    }
                }
                catch (ArgumentException) { }
            }
            catch (Exception ex)
            {
                Log.TraceLine("ProfilesController: Manual Chrome detection error: {0}", ex.Message);
            }
        }

        private bool BringUpProcess(int processId)
        {
            if (!watchedProcesses.TryGetValue(processId, out var profileSettings))
                return false;

            if (CurrentProfileSettings != profileSettings)
            {
                Log.TraceLine("ProfilesController: Foreground changed: {0} => {1}",
                    CurrentProfileSettings?.ProfileName, profileSettings.ProfileName);
                CurrentProfileSettings = profileSettings;
                ProfileChanged(profileSettings);
            }
            return true;
        }

        private string getProfilePrefix() {
            string prefix = "PowerControl.Process";
            if (DisplayConfig.IsExternalConnected.GetValueOrDefault(false)) {
                prefix += ".Docked";
                Log.TraceLine("ProfilesController: DOCKED MODE");
            } else {
                Log.TraceLine("ProfilesController: REGULAR MODE");
            }
            return prefix;
        }

        private void AddProcess(int processId, string processName)
        {
            Log.TraceLine("ProfilesController: New Process: {0}/{1}", processId, processName);

            if (changedSettings == null)
                changedSettings = new Dictionary<MenuItemWithOptions, string>();

            // Check if this is Chrome with GeForce NOW
            string customProcessName = GetCustomProcessName(processId, processName);

            var profileSettings = new ProfileSettings(getProfilePrefix(), customProcessName);
            watchedProcesses.Add(processId, profileSettings);

            ApplyProfile(profileSettings);
        }

        private string GetCustomProcessName(int processId, string processName)
        {
            // Check if this is Chrome with GeForce NOW
            if (processName.Equals("chrome", StringComparison.OrdinalIgnoreCase))
            {
                Log.TraceLine("=== ProfilesController: Chrome process detected (PID: {0}) ===", processId);
                
                try
                {
                    // Check current process
                    var process = Process.GetProcessById(processId);
                    
                    string commandLine = GetCommandLine(process);
                    
                    if (!string.IsNullOrEmpty(commandLine))
                    {
                        string detectedService = DetectCloudGamingService(commandLine);
                        if (!string.IsNullOrEmpty(detectedService))
                        {
                            Log.TraceLine("ProfilesController: ✓ Detected {0} in Chrome (PID: {1})", detectedService, processId);
                            return detectedService;
                        }
                    }
                    
                    // Check all Chrome processes (parent and children)
                    var allChromeProcesses = Process.GetProcessesByName("chrome");
                    
                    int checkedCount = 0;
                    foreach (var chromeProc in allChromeProcesses)
                    {
                        try
                        {
                            string chromeCmdLine = GetCommandLine(chromeProc);
                            if (!string.IsNullOrEmpty(chromeCmdLine))
                            {
                                checkedCount++;
                                string detected = DetectCloudGamingService(chromeCmdLine);
                                if (!string.IsNullOrEmpty(detected))
                                {
                                    Log.TraceLine("ProfilesController: ✓ Found {0} in Chrome process tree (PID: {1})", detected, chromeProc.Id);
                                    return detected;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Ignore exceptions when checking other processes
                        }
                    }
                    
                    // Alternative: Check window title as fallback
                    string windowTitle = GetWindowTitle(processId);
                    if (!string.IsNullOrEmpty(windowTitle))
                    {
                        string serviceFromTitle = DetectCloudGamingServiceFromTitle(windowTitle);
                        if (!string.IsNullOrEmpty(serviceFromTitle))
                        {
                            Log.TraceLine("ProfilesController: ✓ Detected {0} from window title", serviceFromTitle);
                            return serviceFromTitle;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.TraceLine("ProfilesController: Error in GetCustomProcessName for PID {0}: {1}", processId, ex.Message);
                    Log.TraceLine("ProfilesController: Stack trace: {0}", ex.StackTrace);
                }
            }
            
            return processName;
        }
        
        private string DetectCloudGamingService(string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine))
                return string.Empty;
            
            // Check for GeForce NOW URL
            if (commandLine.Contains("play.geforcenow.com", StringComparison.OrdinalIgnoreCase))
            {
                Log.TraceLine("ProfilesController: ✓ Matched 'play.geforcenow.com' in command line");
                return "GeForceNOW";
            }
            
            // Check for Xbox Cloud Gaming
            if (commandLine.Contains("xbox.com/play", StringComparison.OrdinalIgnoreCase) ||
                commandLine.Contains("xbox.com/en-US/play", StringComparison.OrdinalIgnoreCase))
            {
                Log.TraceLine("ProfilesController: ✓ Matched Xbox Cloud Gaming URL in command line");
                return "XboxCloudGaming";
            }
            
            // Check for Amazon Luna
            if (commandLine.Contains("luna.amazon.com", StringComparison.OrdinalIgnoreCase))
            {
                Log.TraceLine("ProfilesController: ✓ Matched 'luna.amazon.com' in command line");
                return "AmazonLuna";
            }
            
            // Check for Stadia (if still active)
            if (commandLine.Contains("stadia.google.com", StringComparison.OrdinalIgnoreCase))
            {
                Log.TraceLine("ProfilesController: ✓ Matched 'stadia.google.com' in command line");
                return "Stadia";
            }
            
            
            return string.Empty;
        }

        private string DetectCloudGamingServiceFromTitle(string windowTitle)
        {
            if (string.IsNullOrEmpty(windowTitle))
                return string.Empty;
            
            // Check for GeForce NOW in title
            if (windowTitle.Contains("GeForce NOW", StringComparison.OrdinalIgnoreCase) ||
                windowTitle.Contains("play.geforcenow.com", StringComparison.OrdinalIgnoreCase))
            {
                return "GeForceNOW";
            }
            
            // Check for Xbox Cloud Gaming
            if (windowTitle.Contains("Xbox", StringComparison.OrdinalIgnoreCase) && 
                windowTitle.Contains("Cloud", StringComparison.OrdinalIgnoreCase))
            {
                return "XboxCloudGaming";
            }
            
            // Check for Amazon Luna
            if (windowTitle.Contains("Luna", StringComparison.OrdinalIgnoreCase) ||
                windowTitle.Contains("Amazon Luna", StringComparison.OrdinalIgnoreCase))
            {
                return "AmazonLuna";
            }
            
            return string.Empty;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private string GetWindowTitle(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    int length = GetWindowTextLength(process.MainWindowHandle);
                    if (length > 0)
                    {
                        StringBuilder sb = new StringBuilder(length + 1);
                        GetWindowText(process.MainWindowHandle, sb, sb.Capacity);
                        return sb.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.TraceLine("ProfilesController: Error getting window title for PID {0}: {1}", processId, ex.Message);
            }
            
            return string.Empty;
        }

        private string GetCommandLine(Process process)
        {
            try
            {
                // Try WMI method first (most reliable but requires permissions)
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
                {
                    using (ManagementObjectCollection objects = searcher.Get())
                    {
                        foreach (ManagementObject obj in objects)
                        {
                            string cmdLine = obj["CommandLine"]?.ToString() ?? string.Empty;
                            if (!string.IsNullOrEmpty(cmdLine))
                            {
                                return cmdLine;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.TraceLine("ProfilesController: WMI query failed for PID {0}: {1}", process.Id, ex.Message);
                
                // Fallback: try to get command line from process StartInfo (less reliable)
                try
                {
                    if (process.StartInfo != null && !string.IsNullOrEmpty(process.StartInfo.Arguments))
                    {
                        return $"{process.MainModule?.FileName} {process.StartInfo.Arguments}";
                    }
                }
                catch { }
            }
            
            return string.Empty;
        }

        private void RemoveProcess(int processId)
        {
            if (!watchedProcesses.Remove(processId, out var profileSettings))
                return;

            // Clean up manual detection tracking
            manuallyDetectedProcesses.Remove(processId);

            if (CurrentProfileSettings == profileSettings)
                CurrentProfileSettings = null;

            Log.TraceLine("ProfilesController: Removed Process: {0}", processId);

            if (watchedProcesses.Any())
                return;

            ResetProfile();
        }

        private void Root_OnOptionValueChange(MenuItemWithOptions options, string? oldValue, string newValue)
        {
            if (options.PersistentKey is null)
                return;

            // No active profile, cannot persist
            if (CurrentProfileSettings is null)
                return;

            // Do not auto-create profile unless requested
            if (!CurrentProfileSettings.Exists && !AutoCreateProfiles)
                return;

            var persistedValue = CurrentProfileSettings.GetValue(options.PersistentKey ?? "");

            if (persistedValue != newValue) {
                CurrentProfileSettings.SetValue(options.PersistentKey, newValue);
                options.ProfileOption = newValue;

                Log.TraceLine("ProfilesController: Stored: {0} {1} = {2}",
                    CurrentProfileSettings.ProfileName, options.PersistentKey, newValue);
            }
        }

        private void ProfileChanged(ProfileSettings? profileSettings)
        {
            foreach (var menuItem in PersistableOptions())
            {
                menuItem.ProfileOption = profileSettings?.GetValue(menuItem.PersistentKey ?? "");
            }
        }

        public void CreateProfile()
        {
            var profileSettings = CurrentProfileSettings;

            profileSettings?.TouchFile();

            Log.TraceLine("ProfilesController: Created Profile: {0}",
                profileSettings?.ProfileName);

            foreach (var menuItem in PersistableOptions())
            {
                if (!menuItem.PersistOnCreate || menuItem.ActiveOption is null)
                    continue;
                profileSettings?.SetValue(menuItem.PersistentKey ?? "", menuItem.ActiveOption);
            }

            ProfileChanged(profileSettings);
        }

        public void DeleteProfile()
        {
            CurrentProfileSettings?.DeleteFile();
            ProfileChanged(CurrentProfileSettings);

            Log.TraceLine("ProfilesController: Deleted Profile: {0}", CurrentProfileSettings?.ProfileName);
        }

        private void ApplyProfile(ProfileSettings profileSettings)
        {
            CurrentProfileSettings = profileSettings;
            ProfileChanged(profileSettings);

            if (CurrentProfileSettings is null || CurrentProfileSettings?.Exists != true)
                return;

            int delay = CurrentProfileSettings.GetInt("ApplyDelay", ApplyProfileDelayMs);

            changeTask?.Cancel();
            changeTask = Dispatcher.RunWithDelay(delay, () =>
            {
                foreach (var menuItem in PersistableOptions())
                {
                    var persistedValue = CurrentProfileSettings.GetValue(menuItem.PersistentKey ?? "");
                    if (persistedValue is null)
                    {
                        continue;
                    }

                    try
                    {
                        menuItem.Set(persistedValue, true, false);

                        Log.TraceLine("ProfilesController: Applied from Profile: {0}: {1} = {2}",
                            CurrentProfileSettings.ProfileName, menuItem.PersistentKey, persistedValue);
                    }
                    catch (Exception e)
                    {
                        Log.TraceLine("ProfilesController: Exception Profile: {0}: {1} = {2} => {3}",
                            CurrentProfileSettings.ProfileName, menuItem.PersistentKey, persistedValue, e);

                        CurrentProfileSettings.DeleteKey(menuItem.PersistentKey ?? "");
                        menuItem.ProfileOption = null;
                    }
                }
            });
        }

        public void ResetProfile()
        {
            CurrentProfileSettings = AutostartProfileSettings;
            ProfileChanged(null);

            if (changedSettings is null)
                return;

            // Revert all changes made to original value
            var appliedSettings = changedSettings;
            changedSettings = null;

            changeTask?.Cancel();
            ApplyProfile(CurrentProfileSettings);
        }

        private IEnumerable<MenuItemWithOptions> PersistableOptions()
        {
            return MenuItemWithOptions.
                Order(MenuStack.Root.AllMenuItemOptions()).
                Where((item) => item.PersistentKey is not null).
                Reverse();
        }
    }
}
