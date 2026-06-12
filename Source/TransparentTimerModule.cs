using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System.Reflection;

namespace Celeste.Mod.TransparentTimer;

public class TransparentTimerModule : EverestModule {
    public static TransparentTimerModule Instance { get; private set; }
    public override Type SettingsType => typeof(TransparentTimerSettings);
    public static TransparentTimerSettings Settings => (TransparentTimerSettings)Instance._Settings;

    [ThreadStatic]
    private static bool _isRenderingTimer;

    private List<ILHook> _hooks = new();

    public TransparentTimerModule() { Instance = this; }

    public override void Load() {
        On.Celeste.SpeedrunTimerDisplay.Render += OnRender;

        // Hook every SpriteBatch.Draw overload that takes a Color.
        // SpriteBatch.Draw is the single convergence point for ALL Monocle
        // drawing (Draw.Rect, MTexture.Draw, PixelFont.DrawOutline, etc.
        // all eventually call it).  No nesting → each Color is modified once.
        foreach (var m in typeof(SpriteBatch).GetMethods(BindingFlags.Instance | BindingFlags.Public)) {
            if (m.Name != "Draw") continue;
            var par = m.GetParameters();
            for (int i = 0; i < par.Length; i++) {
                if (par[i].ParameterType == typeof(Color)) {
                    int ilIdx = i + 1; // instance method: arg 0 = this
                    try { _hooks.Add(new ILHook(m, ModColorArg(ilIdx))); } catch { }
                    break;
                }
            }
        }
    }

    public override void Unload() {
        On.Celeste.SpeedrunTimerDisplay.Render -= OnRender;
        foreach (var h in _hooks) h?.Dispose();
        _hooks.Clear();
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, FMOD.Studio.EventInstance snapshot) {
        menu.Add(new TextMenu.SubHeader(Dialog.Clean("TRANSPARENT_TIMER") + " | v." + Metadata.VersionString));
        menu.Add(new TextMenu.OnOff(Dialog.Clean("TRANSPARENT_TIMER_ENABLED"), Settings.Enabled).Change(v => {
            Settings.Enabled = v; Instance.SaveSettings();
        }));
        menu.Add(new TextMenu.Slider(Dialog.Clean("TRANSPARENT_TIMER_OPACITY"), i => (i * 10) + "%", 0, 10, Settings.Opacity).Change(i => {
            Settings.Opacity = i; Instance.SaveSettings();
        }));
    }

    // ── On hook ────────────────────────────────────────────────────────

    private static void OnRender(On.Celeste.SpeedrunTimerDisplay.orig_Render orig, SpeedrunTimerDisplay self) {
        if (!Settings.Enabled) { orig(self); return; }
        _isRenderingTimer = true;
        orig(self);
        _isRenderingTimer = false;
    }

    // ── IL manipulator ─────────────────────────────────────────────────

    private static ILContext.Manipulator ModColorArg(int ilIdx) => il => {
        var cursor = new ILCursor(il);
        cursor.Emit(OpCodes.Ldarg, ilIdx);
        cursor.EmitDelegate<Func<Color, Color>>(TimerOpacity);
        cursor.Emit(OpCodes.Starg, ilIdx);
    };

    private static Color TimerOpacity(Color c) {
        if (!Settings.Enabled) return c;
        if (!_isRenderingTimer) return c;
        float a = Settings.Opacity / 10f;
        return c * a;
    }
}
