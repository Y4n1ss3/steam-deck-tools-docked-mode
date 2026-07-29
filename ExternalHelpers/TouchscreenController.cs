namespace ExternalHelpers
{
    /// <summary>
    /// Controls the Steam Deck's built-in touchscreen via PowerShell PnP device management.
    /// Disables the touchscreen when an external display is connected, re-enables it on disconnect.
    /// Requires the application to run with administrative privileges.
    /// </summary>
    public static class TouchscreenController
    {
        // Hardware ID for the Steam Deck's built-in touchscreen (Focaltech FTS3528)
        private const string TouchscreenHardwareId = "FTS3528";

        /// <summary>
        /// Attempts to disable the built-in touchscreen.
        /// </summary>
        /// <returns>True if the command was launched successfully, false otherwise.</returns>
        public static bool Disable()
        {
            return SetDeviceState(enable: false);
        }

        /// <summary>
        /// Attempts to enable the built-in touchscreen.
        /// </summary>
        /// <returns>True if the command was launched successfully, false otherwise.</returns>
        public static bool Enable()
        {
            return SetDeviceState(enable: true);
        }

        private static bool SetDeviceState(bool enable)
        {
            string action = enable ? "Enable-PnpDevice" : "Disable-PnpDevice";
            // Target specifically the HID touchscreen child device by InstanceId
            string command = $"Get-PnpDevice | Where-Object {{ $_.InstanceId -match 'FTS3528' -and $_.FriendlyName -match 'tactile|touch' }} | {action} -Confirm:$false";

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
                {
                    Arguments = $"-NonInteractive -NoProfile -Command \"{command}\"",
                    UseShellExecute = false, // Inherit parent admin token (PowerControl already runs as admin)
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                var process = System.Diagnostics.Process.Start(psi);
                process?.WaitForExit(5000);
                return process != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TouchscreenController: Failed to {action} touchscreen: {ex.Message}");
                return false;
            }
        }
    }
}
