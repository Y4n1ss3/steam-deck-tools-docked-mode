using PowerControl.Helpers;
using PowerControl.Helpers.AMD;

namespace PowerControl.Options
{
    public static class RefreshRate
    {
        public static Menu.MenuItemWithOptions Instance = new Menu.MenuItemWithOptions()
        {
            Name = "Refresh Rate",
            PersistentKey = "RefreshRate",
            PersistOnCreate = true,
            ApplyDelay = 1000,
            ResetValue = () => { return "Default"; },
            OptionsValues = delegate ()
            {
                var refreshRates = DisplayResolutionController.GetRefreshRates();
                if (refreshRates.Count() > 1)
                {
                    var options = new List<string> { "Default" };
                    options.AddRange(refreshRates.Select(item => item.ToString()));
                    return options.ToArray();
                }
                return null;
            },
            CurrentValue = delegate ()
            {
                if (Instance.ActiveOption == "Default")
                    return "Default";
                return DisplayResolutionController.GetRefreshRate().ToString();
            },
            ApplyValue = (selected) =>
            {
                if (selected == "Default") return "Default";

                DisplayResolutionController.SetRefreshRate(int.Parse(selected));

                return DisplayResolutionController.GetRefreshRate().ToString();
            },
            Impacts =
            {
                FPSLimit.Instance
            }
        };
    }
}
