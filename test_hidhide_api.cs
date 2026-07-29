using System;
using System.Reflection;
using Nefarius.Drivers.HidHide;

public class Program {
    public static void Main() {
        var methods = typeof(HidHideControlService).GetMethods();
        foreach (var m in methods) {
            Console.WriteLine(m.Name);
        }
    }
}
