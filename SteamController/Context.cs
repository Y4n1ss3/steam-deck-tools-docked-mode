using static CommonHelpers.Log;

namespace SteamController
{
    public partial class Context : IDisposable
    {
        public const double JoystickToMouseSensitivity = 1200;
        public const double PadToMouseSensitivity = 150;
        public const double PadToWhellSensitivity = 4;
        public const double ThumbToWhellSensitivity = 20;

        public Devices.SteamController Steam { get; private set; }
        public Devices.Xbox360Controller X360 { get; private set; }
        public List<Devices.Xbox360Controller> ExtraX360 { get; private set; } = new List<Devices.Xbox360Controller>();
        public Devices.DS4Controller DS4 { get; private set; }
        public Devices.KeyboardController Keyboard { get; private set; }
        public Devices.MouseController Mouse { get; private set; }

        public List<Profiles.Profile> Profiles { get; } = new List<Profiles.Profile>();
        public List<Managers.Manager> Managers { get; } = new List<Managers.Manager>();

        private int selectedProfile;
        private int controllerProfile;

        public struct ContextState
        {
            public bool GameProcessRunning { get; set; }
            public bool RTSSInForeground { get; set; }
            public bool SteamUsesX360Controller { get; set; }
            public bool SteamUsesDS4Controller { get; set; }
            public bool SteamUsesSteamInput { get; set; }

            public bool IsActive
            {
                get { return RTSSInForeground || GameProcessRunning || SteamUsesX360Controller || SteamUsesDS4Controller || SteamUsesSteamInput; }
            }

            public override string ToString()
            {
                string reason = "state";
                if (GameProcessRunning) reason += " game";
                if (SteamUsesX360Controller) reason += " steamX360";
                if (SteamUsesDS4Controller) reason += " steamDS4";
                if (SteamUsesSteamInput) reason += " steamInput";
                if (RTSSInForeground) reason += " rtss";
                return reason;
            }
        }

        public bool RequestEnable { get; set; } = true;
        public ContextState State;

        public event Action<Profiles.Profile> ProfileChanged;
        public Action? SelectDefault;

        public bool Enabled
        {
            get { return RequestEnable; }
        }

        public bool KeyboardMouseValid
        {
            get { return SteamController.Managers.SASManager.Valid; }
        }

        public Profiles.Profile? CurrentProfile
        {
            get
            {
                for (int i = 0; i < Profiles.Count; i++)
                {
                    var profile = Profiles[(selectedProfile + i) % Profiles.Count];
                    if (profile.Selected(this))
                        return profile;
                }

                return null;
            }
        }

        private readonly System.Windows.Threading.Dispatcher _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

        public Context()
        {
            Steam = new Devices.SteamController();
            X360 = new Devices.Xbox360Controller();
            DS4 = new Devices.DS4Controller();
            Keyboard = new Devices.KeyboardController();
            Mouse = new Devices.MouseController();

            ProfileChanged += (_) => X360.Beep();
            ProfileChanged += (profile) => TraceLine("Context: Selected Profile: {0}", profile.Name);
        }

        public void Dispose()
        {
            foreach (var manager in Managers)
                manager.Dispose();

            using (Steam) { }
            using (X360) { }
            foreach (var extra in ExtraX360) using (extra) { }
            using (DS4) { }
            using (Keyboard) { }
            using (Mouse) { }
        }

        public void Tick()
        {
            X360.Tick();
            foreach (var extra in ExtraX360) extra.Tick();
            DS4.Tick();

            foreach (var manager in Managers)
            {
                try { manager.Tick(this); }
                catch (Exception e) { TraceException("Controller", manager, e); }
            }
        }

        public bool Update()
        {
            Steam.BeforeUpdate();
            X360.BeforeUpdate();
            foreach (var extra in ExtraX360) extra.BeforeUpdate();
            DS4.BeforeUpdate();
            Keyboard.BeforeUpdate();
            Mouse.BeforeUpdate();

            try
            {
                HandleExternalGamepads();

                var profile = CurrentProfile;
                if (profile is not null)
                    profile.Run(this);

                // Forward first external controller inputs to ViGEm X360 (after profile, so we win)
                ExternalGamepadPassthrough();

                return true;
            }
            catch (Exception e)
            {
                TraceException("Context", "Update", e);
                return false;
            }
            finally
            {
                Steam.Update();
                X360.Update();
                foreach (var extra in ExtraX360) extra.Update();
                DS4.Update();
                Keyboard.Update();
                Mouse.Update();
            }
        }

        public bool SelectProfile(String name, bool userDefault = false)
        {
            lock (this)
            {
                for (int i = 0; i < Profiles.Count; i++)
                {
                    var profile = Profiles[i];
                    if (profile.Name != name)
                        continue;
                    if (!profile.Selected(this) && !userDefault)
                        continue;

                    if (i != selectedProfile)
                    {
                        selectedProfile = i;
                        if (!profile.IsDesktop && !userDefault)
                            controllerProfile = i;
                        OnProfileChanged(profile);
                    }
                    return true;
                }
            }

            return false;
        }

        public void SelectController()
        {
            lock (this)
            {
                var current = CurrentProfile;
                if (current is null)
                    return;
                if (!current.IsDesktop)
                    return;

                // Use last selected controller profile
                selectedProfile = controllerProfile;
                var currentController = CurrentProfile;
                if (current != currentController && currentController?.IsDesktop != true)
                    return;

                // Otherwise use next one
                TraceLine("Context: SelectController. State={0}", State);
                SelectNext();
            }
        }

        public bool SelectNext()
        {
            lock (this)
            {
                // Update selectedProfile index
                var current = CurrentProfile;
                if (current is null)
                    return false;
                selectedProfile = Profiles.IndexOf(current);

                for (int i = 1; i < Profiles.Count; i++)
                {
                    var idx = (selectedProfile + i) % Profiles.Count;
                    var profile = Profiles[idx];
                    if (profile.IsDesktop)
                        continue;
                    if (!profile.Selected(this))
                        continue;

                    selectedProfile = idx;
                    controllerProfile = idx;
                    OnProfileChanged(profile);
                    return true;
                }
            }

            return false;
        }

        public void BackToDefault()
        {
            TraceLine("Context: Back To Default.");
            if (SelectDefault is not null)
                SelectDefault();
        }

        private void OnProfileChanged(Profiles.Profile profile)
        {
            _dispatcher.BeginInvoke(new Action(() => ProfileChanged(profile)));
        }


        private readonly Dictionary<string, DateTime> selectPressStartTimes = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, bool> selectHoldTriggered = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastSelectStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, DateTime> startPressStartTimes = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, bool> startHoldTriggered = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastDpadUpStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastDpadDownStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastDpadLeftStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastDpadRightStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastButtonXStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastR1States = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastButtonYStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastStartBtnStates = new Dictionary<string, bool>();

        private string GetGamepadId(Windows.Gaming.Input.RawGameController raw)
        {
            try
            {
                if (raw != null && !string.IsNullOrEmpty(raw.NonRoamableId))
                {
                    return raw.NonRoamableId;
                }
            }
            catch { }
            return "gamepad_" + (raw?.GetHashCode() ?? 0);
        }

        private void RunOnSTA(Action action)
        {
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    action();
                }
                catch { }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
        }

        private bool IsOSDVisible()
        {
            try
            {
                if (CommonHelpers.SharedData<CommonHelpers.PowerControlSetting>.GetExistingValue(out var pcSetting))
                {
                    return pcSetting.Current == CommonHelpers.PowerControlVisible.Yes;
                }
            }
            catch { }
            return false;
        }

        private void ExternalGamepadPassthrough()
        {
            // Only active in controller mode (not desktop) and when OSD is hidden
            if (CurrentProfile is Profiles.Predefined.DesktopProfile) return;
            if (IsOSDVisible()) return;

            try
            {
                int padIndex = 0;
                foreach (var raw in Windows.Gaming.Input.RawGameController.RawGameControllers)
                {
                    // Skip emulated controllers
                    if (raw.HardwareVendorId == 0x045E && raw.HardwareProductId == 0x028E) continue;
                    if (raw.HardwareVendorId == 0x054C && raw.HardwareProductId == 0x05C4) continue;
                    
                    // Skip built-in Valve Steam Deck controllers (already handled natively)
                    if (raw.HardwareVendorId == 0x28DE) continue;

                    // Skip phantom devices with 0 inputs
                    if (raw.ButtonCount == 0 && raw.AxisCount == 0 && raw.SwitchCount == 0) continue;

                    var buttons = new bool[raw.ButtonCount];
                    var switches = new Windows.Gaming.Input.GameControllerSwitchPosition[raw.SwitchCount];
                    var axes = new double[raw.AxisCount];
                    raw.GetCurrentReading(buttons, switches, axes);

                    // Pick the target virtual controller
                    Devices.Xbox360Controller targetX360;
                    if (padIndex == 0)
                    {
                        targetX360 = X360;
                    }
                    else
                    {
                        int extraIndex = padIndex - 1;
                        while (ExtraX360.Count <= extraIndex)
                        {
                            var newPad = new Devices.Xbox360Controller();
                            newPad.Tick();
                            ExtraX360.Add(newPad);
                        }
                        targetX360 = ExtraX360[extraIndex];
                    }
                    targetX360.Connected = true;

                    // --- Buttons ---
                    bool isStadia = raw.HardwareVendorId == 0x18D1 && raw.HardwareProductId == 0x9400;

                    if (isStadia)
                    {
                        // Stadia DirectInput mapping
                        if (buttons.Length > 0) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.A] = buttons[0];
                        if (buttons.Length > 1) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.B] = buttons[1];
                        if (buttons.Length > 2) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.X] = buttons[2];
                        if (buttons.Length > 3) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Y] = buttons[3];
                        if (buttons.Length > 4) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.LeftShoulder] = buttons[4];
                        if (buttons.Length > 5) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.RightShoulder] = buttons[5];
                        if (buttons.Length > 6) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Back] = buttons[6];
                        if (buttons.Length > 7) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Start] = buttons[7];
                        if (buttons.Length > 9) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.LeftThumb] = buttons[9];
                        if (buttons.Length > 10) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.RightThumb] = buttons[10];

                        // 8 is Stadia, 11 is Assistant, 12 is Capture
                        bool guidePressed = (buttons.Length > 8 && buttons[8]) || 
                                            (buttons.Length > 11 && buttons[11]) || 
                                            (buttons.Length > 12 && buttons[12]);
                        
                        if (guidePressed)
                            targetX360.Overwrite(Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Guide, true, 100);
                        else
                            targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Guide] = false;
                    }
                    else
                    {
                        // Generic mapping
                        if (buttons.Length > 0) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.A] = buttons[0];
                        if (buttons.Length > 1) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.B] = buttons[1];
                        if (buttons.Length > 2) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.X] = buttons[2];
                        if (buttons.Length > 3) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Y] = buttons[3];
                        if (buttons.Length > 4) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.LeftShoulder] = buttons[4];
                        if (buttons.Length > 5) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.RightShoulder] = buttons[5];
                        if (buttons.Length > 6) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Back] = buttons[6];
                        if (buttons.Length > 7) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Start] = buttons[7];
                        if (buttons.Length > 8) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.LeftThumb] = buttons[8];
                        if (buttons.Length > 9) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.RightThumb] = buttons[9];
                        if (buttons.Length > 10) 
                        {
                            if (buttons[10]) targetX360.Overwrite(Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Guide, true, 100);
                            else targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Guide] = false;
                        }
                    }

                    // --- D-pad via switch ---
                    if (switches.Length > 0)
                    {
                        var d = switches[0];
                        targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Up] =
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.Up ||
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.UpLeft ||
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.UpRight;
                        targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Down] =
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.Down ||
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.DownLeft ||
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.DownRight;
                        targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Left] =
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.Left ||
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.UpLeft ||
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.DownLeft;
                        targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button.Right] =
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.Right ||
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.UpRight ||
                            d == Windows.Gaming.Input.GameControllerSwitchPosition.DownRight;
                    }

                    // --- Axes : [0.0, 1.0] centered at 0.5 → [-32767, 32767] ---
                    static short ToAxis(double v) =>
                        (short)Math.Clamp((v - 0.5) * 2.0 * 32767, short.MinValue, short.MaxValue);
                    static short ToAxisInv(double v) =>
                        (short)Math.Clamp((0.5 - v) * 2.0 * 32767, short.MinValue, short.MaxValue);
                    static short ToTrigger(double v) =>
                        (short)Math.Clamp(v * 32767, 0, short.MaxValue);

                    if (axes.Length > 0) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Axis.LeftThumbX] = ToAxis(axes[0]);
                    if (axes.Length > 1) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Axis.LeftThumbY] = ToAxisInv(axes[1]);
                    if (axes.Length > 2) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Axis.RightThumbX] = ToAxis(axes[2]);
                    if (axes.Length > 3) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Axis.RightThumbY] = ToAxisInv(axes[3]);
                    if (axes.Length > 4) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Slider.LeftTrigger] = ToTrigger(axes[4]);
                    if (axes.Length > 5) targetX360[Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Slider.RightTrigger] = ToTrigger(axes[5]);

                    padIndex++;
                }

                // Disconnect extra pads that are no longer used
                for (int i = padIndex - 1; i < ExtraX360.Count; i++)
                {
                    if (i >= 0) ExtraX360[i].Connected = false;
                }
            }
            catch (Exception ex)
            {
                CommonHelpers.Log.TraceLine($"Context.ExternalGamepadPassthrough error: {ex.Message}");
            }
        }

        private void HandleExternalGamepads()
        {
            try
            {
                bool isDesktop = CurrentProfile is Profiles.Predefined.DesktopProfile;
                bool isOsdVisible = IsOSDVisible();
                bool isPrimaryGamepad = true;

                foreach (var raw in Windows.Gaming.Input.RawGameController.RawGameControllers)
                {
                    // Ignore emulated Xbox 360 controller (VID: 0x045E, PID: 0x028E)
                    if (raw.HardwareVendorId == 0x045E && raw.HardwareProductId == 0x028E)
                        continue;
                    // Ignore emulated DS4 controller (VID: 0x054C, PID: 0x05C4)
                    if (raw.HardwareVendorId == 0x054C && raw.HardwareProductId == 0x05C4)
                        continue;
                        
                    // Skip built-in Valve Steam Deck controllers (already handled natively)
                    if (raw.HardwareVendorId == 0x28DE) continue;

                    // Automatically install/configure HidHide to block the physical controller from Steam/Windows
                    SteamController.Managers.HidHideManager.HideGamepad(raw.HardwareVendorId, raw.HardwareProductId);

                    var buttons = new bool[raw.ButtonCount];
                    var switches = new Windows.Gaming.Input.GameControllerSwitchPosition[raw.SwitchCount];
                    var axes = new double[raw.AxisCount];
                    raw.GetCurrentReading(buttons, switches, axes);

                    string gamepadId = GetGamepadId(raw);

                    // --- 1. Select Hold Shortcut (Always Active) ---
                    // Button 6 is View/Select on standard Xbox controllers
                    bool selectPressed = buttons.Length > 6 && buttons[6];
                    lastSelectStates.TryGetValue(gamepadId, out bool lastSelectPressed);

                    if (isOsdVisible)
                    {
                        // If OSD is visible, a single short press of Select closes it immediately
                        if (selectPressed && !lastSelectPressed)
                        {
                            // Simulate Alt + F11 to toggle the Power Control OSD menu (overlay)
                            Keyboard.KeyPress(new WindowsInput.VirtualKeyCode[] { WindowsInput.VirtualKeyCode.LMENU }, WindowsInput.VirtualKeyCode.F11);
                        }
                        // Clear hold states when OSD is visible
                        selectPressStartTimes.Remove(gamepadId);
                        selectHoldTriggered[gamepadId] = false;
                    }
                    else
                    {
                        // If OSD is not visible, require a 3-second hold to open it
                        if (selectPressed)
                        {
                            if (!selectPressStartTimes.ContainsKey(gamepadId))
                            {
                                selectPressStartTimes[gamepadId] = DateTime.UtcNow;
                                selectHoldTriggered[gamepadId] = false;
                            }
                            else
                            {
                                var holdDuration = DateTime.UtcNow - selectPressStartTimes[gamepadId];
                                if (holdDuration.TotalMilliseconds >= 3000 && !selectHoldTriggered[gamepadId])
                                {
                                    // Simulate Alt + F11 to toggle the Power Control OSD menu (overlay)
                                    Keyboard.KeyPress(new WindowsInput.VirtualKeyCode[] { WindowsInput.VirtualKeyCode.LMENU }, WindowsInput.VirtualKeyCode.F11);
                                    selectHoldTriggered[gamepadId] = true;
                                }
                            }
                        }
                        else
                        {
                            selectPressStartTimes.Remove(gamepadId);
                            selectHoldTriggered.Remove(gamepadId);
                        }
                    }

                    lastSelectStates[gamepadId] = selectPressed;

                    // --- 1.1 Start Hold Shortcut (Switch between Desktop and Controller Profiles) ---
                    // Button 7 is Menu/Start on standard Xbox controllers
                    bool startPressed = buttons.Length > 7 && buttons[7];

                    if (startPressed)
                    {
                        if (!startPressStartTimes.ContainsKey(gamepadId))
                        {
                            startPressStartTimes[gamepadId] = DateTime.UtcNow;
                            startHoldTriggered[gamepadId] = false;
                        }
                        else
                        {
                            var holdDuration = DateTime.UtcNow - startPressStartTimes[gamepadId];
                            if (holdDuration.TotalMilliseconds >= 3000 && !startHoldTriggered[gamepadId])
                            {
                                if (CurrentProfile is Profiles.Predefined.DesktopProfile)
                                {
                                    // Currently Desktop → switch to X360 controller profile
                                    SelectNext();
                                }
                                else
                                {
                                    // Currently in controller mode → force back to Desktop
                                    SelectProfile("Desktop", true);
                                }
                                startHoldTriggered[gamepadId] = true;
                            }
                        }
                    }
                    else
                    {
                        startPressStartTimes.Remove(gamepadId);
                        startHoldTriggered.Remove(gamepadId);
                    }

                    // --- 2. D-pad Navigation (Active when OSD is visible) ---
                    if (isOsdVisible)
                    {
                        var dpadState = switches.Length > 0 ? switches[0] : Windows.Gaming.Input.GameControllerSwitchPosition.Center;
                        bool dpadUp = dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.Up ||
                                      dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.UpLeft ||
                                      dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.UpRight;
                        bool dpadDown = dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.Down ||
                                        dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.DownLeft ||
                                        dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.DownRight;
                        bool dpadLeft = dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.Left ||
                                        dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.DownLeft ||
                                        dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.UpLeft;
                        bool dpadRight = dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.Right ||
                                         dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.DownRight ||
                                         dpadState == Windows.Gaming.Input.GameControllerSwitchPosition.UpRight;

                        lastDpadUpStates.TryGetValue(gamepadId, out bool lastUp);
                        if (dpadUp && !lastUp)
                            Keyboard.KeyPress(new WindowsInput.VirtualKeyCode[] { WindowsInput.VirtualKeyCode.LCONTROL, WindowsInput.VirtualKeyCode.LWIN }, WindowsInput.VirtualKeyCode.NUMPAD8);
                        lastDpadUpStates[gamepadId] = dpadUp;

                        lastDpadDownStates.TryGetValue(gamepadId, out bool lastDown);
                        if (dpadDown && !lastDown)
                            Keyboard.KeyPress(new WindowsInput.VirtualKeyCode[] { WindowsInput.VirtualKeyCode.LCONTROL, WindowsInput.VirtualKeyCode.LWIN }, WindowsInput.VirtualKeyCode.NUMPAD2);
                        lastDpadDownStates[gamepadId] = dpadDown;

                        lastDpadLeftStates.TryGetValue(gamepadId, out bool lastLeft);
                        if (dpadLeft && !lastLeft)
                            Keyboard.KeyPress(new WindowsInput.VirtualKeyCode[] { WindowsInput.VirtualKeyCode.LCONTROL, WindowsInput.VirtualKeyCode.LWIN }, WindowsInput.VirtualKeyCode.NUMPAD4);
                        lastDpadLeftStates[gamepadId] = dpadLeft;

                        lastDpadRightStates.TryGetValue(gamepadId, out bool lastRight);
                        if (dpadRight && !lastRight)
                            Keyboard.KeyPress(new WindowsInput.VirtualKeyCode[] { WindowsInput.VirtualKeyCode.LCONTROL, WindowsInput.VirtualKeyCode.LWIN }, WindowsInput.VirtualKeyCode.NUMPAD6);
                        lastDpadRightStates[gamepadId] = dpadRight;
                    }

                    // --- 3. Mouse & Keyboard Emulation (Active only in Desktop Mode and when OSD is not visible) ---
                    if (isPrimaryGamepad && isDesktop && !isOsdVisible)
                    {
                        // Custom Select Modifiers for Desktop
                        if (selectPressed)
                        {
                            bool r1Pressed = buttons.Length > 5 && buttons[5];
                            bool yPressed = buttons.Length > 3 && buttons[3];
                            bool startBtnPressed = buttons.Length > 7 && buttons[7]; // Not to be confused with startHold

                            lastR1States.TryGetValue(gamepadId, out bool lastR1);
                            if (r1Pressed && !lastR1) Keyboard.KeyPress(WindowsInput.VirtualKeyCode.LMENU, WindowsInput.VirtualKeyCode.TAB);
                            lastR1States[gamepadId] = r1Pressed;

                            lastButtonYStates.TryGetValue(gamepadId, out bool lastY);
                            if (yPressed && !lastY) Keyboard.KeyPress(new WindowsInput.VirtualKeyCode[] { WindowsInput.VirtualKeyCode.LCONTROL, WindowsInput.VirtualKeyCode.SHIFT }, WindowsInput.VirtualKeyCode.ESCAPE);
                            lastButtonYStates[gamepadId] = yPressed;

                            lastStartBtnStates.TryGetValue(gamepadId, out bool lastStartBtn);
                            if (startBtnPressed && !lastStartBtn) Keyboard.KeyPress(WindowsInput.VirtualKeyCode.LMENU, WindowsInput.VirtualKeyCode.F4);
                            lastStartBtnStates[gamepadId] = startBtnPressed;
                            
                            // Prevent other actions if holding select
                            isPrimaryGamepad = false;
                            continue;
                        }

                        // Left Stick (axes[0] and axes[1])
                        double rawX = axes.Length > 0 ? (axes[0] - 0.5) * 2.0 : 0.0;
                        double rawY = axes.Length > 1 ? (axes[1] - 0.5) * 2.0 : 0.0;

                        double deadzone = 0.10;
                        double x = 0.0;
                        double y = 0.0;

                        if (Math.Abs(rawX) > deadzone)
                        {
                            x = (rawX - Math.Sign(rawX) * deadzone) / (1.0 - deadzone);
                        }
                        if (Math.Abs(rawY) > deadzone)
                        {
                            y = (rawY - Math.Sign(rawY) * deadzone) / (1.0 - deadzone);
                        }

                        double deltaTime = Steam.DeltaTime;
                        if (deltaTime <= 0.0) deltaTime = 0.016;

                        double moveSpeed = 1200.0;
                        double deltaX = x * moveSpeed * deltaTime;
                        double deltaY = y * moveSpeed * deltaTime;

                        if (deltaX != 0.0 || deltaY != 0.0)
                        {
                            Mouse.MoveBy(deltaX, deltaY);
                        }

                        // Right Stick (axes[2] and axes[3]) for scrolling
                        double rawScrollX = axes.Length > 2 ? (axes[2] - 0.5) * 2.0 : 0.0;
                        double rawScrollY = axes.Length > 3 ? (axes[3] - 0.5) * 2.0 : 0.0;
                        double scrollX = 0.0;
                        double scrollY = 0.0;
                        
                        if (Math.Abs(rawScrollX) > deadzone) scrollX = (rawScrollX - Math.Sign(rawScrollX) * deadzone) / (1.0 - deadzone);
                        if (Math.Abs(rawScrollY) > deadzone) scrollY = (rawScrollY - Math.Sign(rawScrollY) * deadzone) / (1.0 - deadzone);

                        double scrollSpeed = Context.ThumbToWhellSensitivity * 2.0;
                        if (scrollX != 0.0) Mouse.HorizontalScroll(scrollX * scrollSpeed * deltaTime);
                        if (scrollY != 0.0) Mouse.VerticalScroll(-scrollY * scrollSpeed * deltaTime * (double)Settings.Default.ScrollDirection);

                        // Button A (buttons[0]) -> Left Click, Button B (buttons[1]) -> Right Click
                        if (buttons.Length > 0 && buttons[0])
                        {
                            Mouse[Devices.MouseController.Button.Left] = true;
                        }
                        if (buttons.Length > 1 && buttons[1])
                        {
                            Mouse[Devices.MouseController.Button.Right] = true;
                        }

                        // Button X (buttons[2]) -> Toggle Windows touch keyboard
                        bool xPressed = buttons.Length > 2 && buttons[2];
                        lastButtonXStates.TryGetValue(gamepadId, out bool lastXState);
                        if (xPressed && !lastXState)
                        {
                            RunOnSTA(() =>
                            {
                                if (!ExternalHelpers.OnScreenKeyboard.Toggle())
                                {
                                    Keyboard.KeyPress(new WindowsInput.VirtualKeyCode[] { WindowsInput.VirtualKeyCode.LCONTROL, WindowsInput.VirtualKeyCode.LWIN }, WindowsInput.VirtualKeyCode.VK_O);
                                }
                            });
                        }
                        lastButtonXStates[gamepadId] = xPressed;
                    }
                    
                    isPrimaryGamepad = false;
                }
            }
            catch (Exception ex)
            {
                CommonHelpers.Log.TraceLine($"Context.HandleExternalGamepads error: {ex.Message}");
            }
        }
    }
}
