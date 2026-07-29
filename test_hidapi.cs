using System;
using HidApi;

public class Program {
    public static void Main() {
        ushort vendorId = 0x18D1;
        ushort productId = 0x9400;
        try {
            foreach (var dev in Hid.Enumerate(vendorId, productId)) {
                Console.WriteLine("HID PATH: " + dev.Path);
            }
        } catch (Exception ex) {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
