using WindowsInput;

namespace SteamController.Profiles.Predefined
{
    public sealed class DesktopProfile : Default.BackPanelShortcutsProfile
    {
        private const String Consumed = "DesktopProfileOwner";
        public DesktopProfile()
        {
            IsDesktop = true;
        }

        public override System.Drawing.Icon Icon
        {
            get
            {
                if (CommonHelpers.WindowsDarkMode.IsDarkModeEnabled)
                    return Resources.monitor_white;
                else
                    return Resources.monitor;
            }
        }

        internal override ProfilesSettings.BackPanelSettings BackPanelSettings
        {
            get { return ProfilesSettings.DesktopPanelSettings.Default; }
        }

        public override bool Selected(Context context)
        {
            return context.Enabled;
        }

        public override Status Run(Context c)
        {
            if (base.Run(c).IsDone)
            {
                return Status.Done;
            }

            // Custom Desktop Shortcuts via Select (BtnMenu)
            if (c.Steam.BtnMenu.Hold(TimeSpan.FromMilliseconds(100), Consumed))
            {
                if (c.Steam.BtnR1.Pressed())
                {
                    c.Keyboard.KeyPress(VirtualKeyCode.LMENU, VirtualKeyCode.TAB);
                }
                if (c.Steam.BtnOptions.Pressed())
                {
                    c.Keyboard.KeyPress(VirtualKeyCode.LMENU, VirtualKeyCode.F4);
                }
                if (c.Steam.BtnY.Pressed())
                {
                    c.Keyboard.KeyPress(new VirtualKeyCode[] { VirtualKeyCode.LCONTROL, VirtualKeyCode.SHIFT }, VirtualKeyCode.ESCAPE);
                }
                return Status.Done;
            }

            if (!c.KeyboardMouseValid)
            {
                // Failed to acquire secure context
                // Enable emergency Lizard
                c.Steam.LizardButtons = true;
                c.Steam.LizardMouse = true;
            }
            else
            {
                c.Steam.LizardButtons = SettingsDebug.Default.LizardButtons;
                c.Steam.LizardMouse = SettingsDebug.Default.LizardMouse;
            }

            EmulateScrollOnLPad(c);
            EmulateScrollOnRStick(c);
            EmulateMouseOnRPad(c);
            EmulateMouseOnLStick(c);
            EmulateDPadArrows(c);

            c.Keyboard[VirtualKeyCode.RETURN] = c.Steam.BtnA;
            c.Keyboard[VirtualKeyCode.BACK] = c.Steam.BtnB;

            return Status.Continue;
        }

        private void EmulateMouseOnLStick(Context c)
        {
            if (c.Steam.LeftThumbX || c.Steam.LeftThumbY)
            {
                c.Mouse.MoveBy(
                    c.Steam.LeftThumbX.GetDeltaValue(
                        Context.JoystickToMouseSensitivity,
                        Devices.DeltaValueMode.AbsoluteTime,
                        Settings.Default.DesktopJoystickDeadzone
                    ),
                    -c.Steam.LeftThumbY.GetDeltaValue(
                        Context.JoystickToMouseSensitivity,
                        Devices.DeltaValueMode.AbsoluteTime,
                        Settings.Default.DesktopJoystickDeadzone
                    )
                );
            }
        }

        private void EmulateScrollOnRStick(Context c)
        {
            if (c.Steam.RightThumbX)
            {
                c.Mouse.HorizontalScroll(
                    c.Steam.RightThumbX.GetDeltaValue(
                        Context.ThumbToWhellSensitivity,
                        Devices.DeltaValueMode.AbsoluteTime,
                        Settings.Default.DesktopJoystickDeadzone
                    )
                );
            }
            if (c.Steam.RightThumbY)
            {
                c.Mouse.VerticalScroll(
                    c.Steam.RightThumbY.GetDeltaValue(
                        Context.ThumbToWhellSensitivity * (double)Settings.Default.ScrollDirection,
                        Devices.DeltaValueMode.AbsoluteTime,
                        Settings.Default.DesktopJoystickDeadzone
                    )
                );
            }
        }


        private void EmulateDPadArrows(Context c)
        {
            c.Keyboard[VirtualKeyCode.LEFT] = c.Steam.BtnDpadLeft;
            c.Keyboard[VirtualKeyCode.RIGHT] = c.Steam.BtnDpadRight;
            c.Keyboard[VirtualKeyCode.UP] = c.Steam.BtnDpadUp;
            c.Keyboard[VirtualKeyCode.DOWN] = c.Steam.BtnDpadDown;
        }
    }
}
