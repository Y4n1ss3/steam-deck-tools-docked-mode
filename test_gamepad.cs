using System;
using System.Threading.Tasks;
using Windows.Gaming.Input;
using System.Threading;

public class Program {
    public static void Main() {
        Console.WriteLine("Waiting for RawGameControllers...");
        Thread.Sleep(2000); // Wait for initialization
        var controllers = RawGameController.RawGameControllers;
        Console.WriteLine($"Found {controllers.Count} controllers.");
        foreach(var c in controllers) {
            Console.WriteLine($"VID: {c.HardwareVendorId:X4}, PID: {c.HardwareProductId:X4}, Buttons: {c.ButtonCount}, Axes: {c.AxisCount}, Switches: {c.SwitchCount}");
            var btns = new bool[c.ButtonCount];
            var sw = new GameControllerSwitchPosition[c.SwitchCount];
            var ax = new double[c.AxisCount];
            c.GetCurrentReading(btns, sw, ax);
            Console.WriteLine("  Buttons: " + string.Join(", ", btns));
            Console.WriteLine("  Axes: " + string.Join(", ", ax));
        }
    }
}
