using System;
using System.Management;

public class Program {
    public static void Main() {
        ushort vendorId = 0x18D1;
        ushort productId = 0x9400;
        string vidHex = vendorId.ToString("X4").ToUpper();
        string pidHex = productId.ToString("X4").ToUpper();
        string queryStr = "SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE '%HID\\%' OR DeviceID LIKE '%BTHLEDEVICE\\%' OR DeviceID LIKE '%USB\\%'";
        using (var searcher = new ManagementObjectSearcher(queryStr))
        using (var collection = searcher.Get()) {
            foreach (ManagementObject device in collection) {
                string instanceId = device["DeviceID"] != null ? device["DeviceID"].ToString().ToUpper() : "\"";
                if (string.IsNullOrEmpty(instanceId)) continue;
                if ((instanceId.Contains(String.Format("VID_{0}", vidHex)) || instanceId.Contains(String.Format("VID&02{0}", vidHex)) || instanceId.Contains(String.Format("VID&01{0}", vidHex)) || instanceId.Contains(String.Format("VID&00{0}", vidHex)) || instanceId.Contains(String.Format("VID&{0}", vidHex))) &&
                    (instanceId.Contains(String.Format("PID_{0}", pidHex)) || instanceId.Contains(String.Format("PID&{0}", pidHex))))
                {
                    Console.WriteLine("MATCHED: " + instanceId);
                }
            }
        }
    }
}
