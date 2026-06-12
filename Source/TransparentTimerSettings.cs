namespace Celeste.Mod.TransparentTimer;

[SettingName("Transparent Timer")]
public class TransparentTimerSettings : EverestModuleSettings {

    [SettingName("Enabled")]
    [SettingSubText("Turn off to revert to the vanilla timer")]
    public bool Enabled { get; set; } = true;

    [SettingName("Opacity")]
    [SettingSubText("Timer opacity (0–100% in steps of 10%)")]
    [SettingRange(0, 10)]
    public int Opacity { get; set; } = 10;
}
