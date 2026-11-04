namespace Pad.Core;

sealed class SettingsModule(Launcher launcher) : ISettingsService
{
    public PadSettings Settings() => launcher.Settings();
}
