using System;
using System.Runtime.InteropServices;
using System.Threading;

public class Program {
    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [DllImport("xinput1_4.dll")]
    public static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

    public static void Main() {
        for(uint i=0; i<4; i++) {
            XINPUT_STATE state;
            if (XInputGetState(i, out state) == 0) {
                Console.WriteLine("Found XInput device at index " + i);
            }
        }
    }
}
