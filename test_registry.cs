using System;
using Microsoft.Win32;

public class Program {
    public static void Main() {
        ushort vendorId = 0x18D1;
        ushort productId = 0x9400;
        string vidHex = vendorId.ToString("X4").ToUpper();
        string pidHex = productId.ToString("X4").ToUpper();

        using (var enumKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Enum\\HID")) {
            if (enumKey != null) {
                foreach (var hwId in enumKey.GetSubKeyNames()) {
                    if ((hwId.Contains($"VID_{vidHex}") || hwId.Contains($"VID&02{vidHex}") || hwId.Contains($"VID&01{vidHex}") || hwId.Contains($"VID&00{vidHex}") || hwId.Contains($"VID&{vidHex}")) &&
                        (hwId.Contains($"PID_{pidHex}") || hwId.Contains($"PID&{pidHex}")))
                    {
                        using (var hwKey = enumKey.OpenSubKey(hwId)) {
                            foreach (var instId in hwKey.GetSubKeyNames()) {
                                string instancePath = $"HID\\{hwId}\\{instId}";
                                Console.WriteLine("MATCHED: " + instancePath);
                            }
                        }
                    }
                }
            }
        }
    }
}
