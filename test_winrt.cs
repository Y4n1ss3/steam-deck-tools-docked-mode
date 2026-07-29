using System;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

public class Program {
    public static async Task Main() {
        var devices = await DeviceInformation.FindAllAsync();
        foreach (var dev in devices) {
            if (dev.Id.ToUpper().Contains("18D1") && dev.Id.ToUpper().Contains("9400")) {
                Console.WriteLine("MATCHED: " + dev.Id);
            }
        }
    }
}
