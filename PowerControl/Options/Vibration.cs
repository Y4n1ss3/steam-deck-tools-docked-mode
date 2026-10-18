using CommonHelpers;

namespace PowerControl.Options
{
    public static class Vibration
    {
        public static Menu.MenuItemWithOptions Instance = new Menu.MenuItemWithOptions()
        {
            Name = "Vibration",
            PersistentKey = "Vibration",
            PersistOnCreate = true,
            ApplyDelay = 500,
            Options = { "No", "Yes" },
            ResetValue = () => "No",
            CurrentValue = delegate ()
            {
                if (SharedData<VibrationSetting>.GetExistingValue(out var value))
                    return value.Current == VibrationMode.Yes ? "Yes" : "No";
                return "No";
            },
            ApplyValue = (selected) =>
            {
                bool enable = selected.Equals("Yes", StringComparison.OrdinalIgnoreCase) || 
                              selected.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                              selected.Equals("1");

                var desired = enable ? VibrationMode.Yes : VibrationMode.No;

                if (SharedData<VibrationSetting>.GetExistingValue(out var value))
                {
                    value.Desired = desired;
                    value.Current = desired;
                    SharedData<VibrationSetting>.SetExistingValue(value);
                }
                else
                {
                    using var shared = SharedData<VibrationSetting>.CreateNew();
                    shared.SetValue(new VibrationSetting
                    {
                        Current = desired,
                        Desired = desired
                    });
                }

                return enable ? "Yes" : "No";
            }
        };
    }
}
