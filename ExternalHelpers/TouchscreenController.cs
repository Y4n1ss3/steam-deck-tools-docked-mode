using System.Runtime.InteropServices;
using System.Text;

namespace ExternalHelpers
{
    public static class TouchscreenController
    {
        // SetupAPI constants
        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_ALLCLASSES = 0x00000004;
        private const uint DICS_ENABLE = 0x00000001;
        private const uint DICS_DISABLE = 0x00000002;
        private const uint DIF_PROPERTYCHANGE = 0x00000012;
        private const uint DICS_FLAG_GLOBAL = 0x00000001;
        private const uint SPDRP_DEVICEDESC = 0x00000000;
        private const uint SPDRP_FRIENDLYNAME = 0x0000000C;
        private static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_CLASSINSTALL_HEADER
        {
            public uint cbSize;
            public uint InstallFunction;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_PROPCHANGE_PARAMS
        {
            public SP_CLASSINSTALL_HEADER ClassInstallHeader;
            public uint StateChange;
            public uint Scope;
            public uint HwProfile;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            IntPtr ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(
            IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData,
            uint Property, out uint PropertyRegDataType,
            StringBuilder PropertyBuffer, uint PropertyBufferSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiSetClassInstallParams(
            IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData,
            ref SP_PROPCHANGE_PARAMS ClassInstallParams, uint ClassInstallParamsSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiCallClassInstaller(
            uint InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        /// <summary>
        /// Enables or disables all touchscreen devices (any device with "touch" in its name).
        /// Requires the app to run with administrator privileges.
        /// </summary>
        public static bool SetEnabled(bool enabled)
        {
            IntPtr devInfoSet = SetupDiGetClassDevs(
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);

            if (devInfoSet == INVALID_HANDLE || devInfoSet == IntPtr.Zero)
            {
                System.Diagnostics.Trace.WriteLine($"TouchscreenController: SetupDiGetClassDevs failed: {Marshal.GetLastWin32Error()}");
                return false;
            }

            try
            {
                bool anyFound = false;
                var devInfoData = new SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>()
                };

                for (uint i = 0; SetupDiEnumDeviceInfo(devInfoSet, i, ref devInfoData); i++)
                {
                    string deviceName = GetDeviceName(devInfoSet, ref devInfoData);
                    if (!deviceName.Contains("touch", StringComparison.OrdinalIgnoreCase))
                        continue;

                    System.Diagnostics.Trace.WriteLine($"TouchscreenController: Found touch device: \"{deviceName}\" → {(enabled ? "enable" : "disable")}");

                    var propChangeParams = new SP_PROPCHANGE_PARAMS
                    {
                        ClassInstallHeader = new SP_CLASSINSTALL_HEADER
                        {
                            cbSize = (uint)Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                            InstallFunction = DIF_PROPERTYCHANGE
                        },
                        StateChange = enabled ? DICS_ENABLE : DICS_DISABLE,
                        Scope = DICS_FLAG_GLOBAL,
                        HwProfile = 0
                    };

                    if (!SetupDiSetClassInstallParams(devInfoSet, ref devInfoData,
                        ref propChangeParams, (uint)Marshal.SizeOf<SP_PROPCHANGE_PARAMS>()))
                    {
                        System.Diagnostics.Trace.WriteLine($"TouchscreenController: SetupDiSetClassInstallParams failed for \"{deviceName}\": {Marshal.GetLastWin32Error()}");
                        continue;
                    }

                    if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, devInfoSet, ref devInfoData))
                    {
                        System.Diagnostics.Trace.WriteLine($"TouchscreenController: SetupDiCallClassInstaller failed for \"{deviceName}\": {Marshal.GetLastWin32Error()}");
                        continue;
                    }

                    anyFound = true;
                    System.Diagnostics.Trace.WriteLine($"TouchscreenController: \"{deviceName}\" {(enabled ? "enable" : "disable")}d successfully.");
                }

                return anyFound;
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }
        }

        private static string GetDeviceName(IntPtr devInfoSet, ref SP_DEVINFO_DATA devInfoData)
        {
            var sb = new StringBuilder(256);
            // Try FriendlyName first, fall back to DeviceDesc
            if (!SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData,
                SPDRP_FRIENDLYNAME, out _, sb, 256, out _))
            {
                SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData,
                    SPDRP_DEVICEDESC, out _, sb, 256, out _);
            }
            return sb.ToString();
        }
    }
}
