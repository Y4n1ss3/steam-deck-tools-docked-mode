using CommonHelpers;

namespace SteamController.Managers
{
    public sealed class SharedDataManager : Manager
    {
        SharedData<SteamControllerSetting> sharedData = SharedData<SteamControllerSetting>.CreateNew();
        SharedData<VibrationSetting> vibrationSharedData = SharedData<VibrationSetting>.CreateNew();

        public override void Tick(Context context)
        {
            if (sharedData.GetValue(out var value) && value.DesiredProfile != "")
            {
                context.SelectProfile(value.DesiredProfile);
            }

            if (vibrationSharedData.GetValue(out var vibValue))
            {
                if (vibValue.Desired != vibValue.Current)
                {
                    vibValue.Current = vibValue.Desired;
                    vibrationSharedData.SetValue(vibValue);
                }
                context.VibrationEnabled = (vibValue.Current == VibrationMode.Yes);
            }

            sharedData.SetValue(new SteamControllerSetting()
            {
                CurrentProfile = context.CurrentProfile?.Name ?? "",
                SelectableProfiles = SelectableProfiles(context).JoinWithN(),
            });
        }

        private IEnumerable<String> SelectableProfiles(Context context)
        {
            return context.Profiles.
                Where((profile) => profile.Selected(context) || profile.Visible).
                Select((profile) => profile.Name);
        }
    }
}
