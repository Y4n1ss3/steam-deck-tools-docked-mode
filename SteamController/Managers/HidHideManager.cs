using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Nefarius.Drivers.HidHide;

namespace SteamController.Managers
{
    public static class HidHideManager
    {
        private static bool _installAttempted = false;
        private static Lazy<HidHideControlService> service = new Lazy<HidHideControlService>(() => new HidHideControlService());

        public static bool IsInstalled()
        {
            try
            {
                string cliPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe");
                return File.Exists(cliPath);
            }
            catch
            {
                return false;
            }
        }

        public static void EnsureInstalled()
        {
            if (_installAttempted) return;
            if (IsInstalled()) return;

            _installAttempted = true;
            CommonHelpers.Log.TraceLine("HidHide is not installed. Extracting and installing from resources...");
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("HidHide.exe"));
                if (resourceName == null)
                {
                    CommonHelpers.Log.TraceLine("HidHide installer not found in resources.");
                    return;
                }

                string tempPath = Path.Combine(Path.GetTempPath(), "SteamDeckTools_HidHide_Installer.exe");

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return;
                    using (FileStream fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fileStream);
                    }
                }

                // Run installer silently
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true, // Required for UAC prompt if needed
                    Verb = "runas"
                });

                process?.WaitForExit();

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                // Re-initialize the service connection after installation
                service = new Lazy<HidHideControlService>(() => new HidHideControlService());
                CommonHelpers.Log.TraceLine("HidHide installation finished.");
            }
            catch (Exception ex)
            {
                CommonHelpers.Log.TraceException("HidHideManager", "EnsureInstalled", ex);
            }
        }

        private static readonly System.Collections.Generic.HashSet<string> _hiddenDevices = new System.Collections.Generic.HashSet<string>();

        public static void HideGamepad(ushort vendorId, ushort productId)
        {
            EnsureInstalled();

            string deviceKey = $"{vendorId:X4}:{productId:X4}";
            if (_hiddenDevices.Contains(deviceKey)) return;
            _hiddenDevices.Add(deviceKey);

            if (!IsInstalled()) return;

            try
            {
                var s = service.Value;
                
                // Whitelist our application
                string processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    var allowed = s.ApplicationPaths;
                    if (!allowed.Contains(processPath))
                    {
                        s.AddApplicationPath(processPath);
                        CommonHelpers.Log.TraceLine($"HidHide: Whitelisted {processPath}");
                    }
                }

                // Enable cloaking globally
                if (!s.IsActive)
                {
                    s.IsActive = true;
                    CommonHelpers.Log.TraceLine("HidHide: Cloaking enabled.");
                }

                // Find matching devices via WMI
                string vidHex = vendorId.ToString("X4").ToUpper();
                string pidHex = productId.ToString("X4").ToUpper();

                // Find all PNP HID devices via Registry to ensure we get Device Instance Paths (not WinRT Device Interface Paths)
                var blocked = s.BlockedInstanceIds;
                
                using (var enumKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\HID"))
                {
                    if (enumKey != null)
                    {
                        foreach (var hwId in enumKey.GetSubKeyNames())
                        {
                            string hwIdUpper = hwId.ToUpper();
                            if ((hwIdUpper.Contains($"VID_{vidHex}") || hwIdUpper.Contains($"VID&02{vidHex}") || hwIdUpper.Contains($"VID&01{vidHex}") || hwIdUpper.Contains($"VID&00{vidHex}") || hwIdUpper.Contains($"VID&{vidHex}")) &&
                                (hwIdUpper.Contains($"PID_{pidHex}") || hwIdUpper.Contains($"PID&{pidHex}")))
                            {
                                using (var hwKey = enumKey.OpenSubKey(hwId))
                                {
                                    if (hwKey != null)
                                    {
                                        foreach (var instId in hwKey.GetSubKeyNames())
                                        {
                                            string instancePath = $@"HID\{hwId}\{instId}".ToUpper();
                                            if (!blocked.Contains(instancePath))
                                            {
                                                s.AddBlockedInstanceId(instancePath);
                                                CommonHelpers.Log.TraceLine("HidHide: Blocked physical device {0}", instancePath);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CommonHelpers.Log.TraceException("HidHideManager", "HideGamepad", ex);
            }
        }
    }
}
