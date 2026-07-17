using System.Diagnostics;
using SteamController.Helpers;

namespace SteamController.Managers
{
    public sealed class ProfileSwitcher : Manager
    {
        private Context.ContextState wasState;

        public override void Tick(Context context)
        {
            if (wasState.Equals(context.State))
                return;

            bool wasActive = wasState.IsActive;
            bool isActive = context.State.IsActive;

            if (isActive && !wasActive)
                context.SelectController();
            else if (!isActive && wasActive)
                context.BackToDefault();

            wasState = context.State;
        }
    }
}
