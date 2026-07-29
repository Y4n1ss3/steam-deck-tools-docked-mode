using System.Diagnostics;
using ExternalHelpers;
using PowerControl.Helpers;
using WindowsInput;

namespace SteamController.Profiles.Default
{
    public abstract class GuideShortcutsProfile : ShortcutsProfile
    {
        public readonly TimeSpan HoldForKill = TimeSpan.FromSeconds(3);
        public readonly TimeSpan HoldForClose = TimeSpan.FromSeconds(1);

        protected override bool SteamShortcuts(Context c)
        {
            if (base.SteamShortcuts(c))
                return true;

            c.Steam.LizardButtons = SettingsDebug.Default.LizardButtons;
            c.Steam.LizardMouse = SettingsDebug.Default.LizardMouse;

            EmulateScrollOnLPad(c);
            EmulateMouseOnRPad(c);

            if (c.Steam.BtnX.Pressed())
            {
                switch (Settings.Default.KeyboardStyle)
                {
                    case Settings.KeyboardStyles.CTRL_WIN_O:
                        c.Keyboard.KeyPress(new VirtualKeyCode[] { VirtualKeyCode.LCONTROL, VirtualKeyCode.LWIN }, VirtualKeyCode.VK_O);
                        break;

                    case Settings.KeyboardStyles.WindowsTouch:
                        if (!OnScreenKeyboard.Toggle())
                        {
                            // Fallback to CTRL+WIN+O
                            c.Keyboard.KeyPress(new VirtualKeyCode[] { VirtualKeyCode.LCONTROL, VirtualKeyCode.LWIN }, VirtualKeyCode.VK_O);
                        }
                        break;
                }
            }

            return true;
        }

        protected void EmulateScrollOnLPad(Context c)
        {
            if (c.Steam.LPadX)
            {
                c.Mouse.HorizontalScroll(
                    c.Steam.LPadX.GetDeltaValue(
                        Context.PadToWhellSensitivity,
                        Devices.DeltaValueMode.Delta,
                        10
                    )
                );
            }
            if (c.Steam.LPadY)
            {
                c.Mouse.VerticalScroll(
                    c.Steam.LPadY.GetDeltaValue(
                        Context.PadToWhellSensitivity * (double)Settings.Default.ScrollDirection,
                        Devices.DeltaValueMode.Delta,
                        10
                    )
                );
            }
        }

        protected void EmulateMouseOnRPad(Context c, bool useButtonTriggers = true)
        {
            if (useButtonTriggers)
            {
                c.Mouse[Devices.MouseController.Button.Right] = c.Steam.BtnL2 || c.Steam.BtnLPadPress;
                c.Mouse[Devices.MouseController.Button.Left] = c.Steam.BtnR2 || c.Steam.BtnRPadPress;
            }
            else
            {
                c.Mouse[Devices.MouseController.Button.Right] = c.Steam.BtnLPadPress;
                c.Mouse[Devices.MouseController.Button.Left] = c.Steam.BtnRPadPress;
            }

            if (c.Steam.RPadX || c.Steam.RPadY)
            {
                c.Mouse.MoveBy(
                    c.Steam.RPadX.GetDeltaValue(Context.PadToMouseSensitivity, Devices.DeltaValueMode.Delta, 10),
                    -c.Steam.RPadY.GetDeltaValue(Context.PadToMouseSensitivity, Devices.DeltaValueMode.Delta, 10)
                );
            }
        }
    }
}
