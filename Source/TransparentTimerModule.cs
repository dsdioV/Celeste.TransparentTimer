using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.TransparentTimer;

public class TransparentTimerModule : EverestModule {
    public static TransparentTimerModule Instance { get; private set; }
    public override Type SettingsType => typeof(TransparentTimerSettings);
    public static TransparentTimerSettings Settings => (TransparentTimerSettings)Instance._Settings;

    private static bool _isRenderingTimer;
    private ILHook _mtexDrawHook;

    public TransparentTimerModule() { Instance = this; }

    public override void Load() {
        // Timer's own methods — no guard needed; all Colors belong to the timer.
        IL.Celeste.SpeedrunTimerDisplay.Render += ModColors(ApplyOpacity);
        IL.Celeste.SpeedrunTimerDisplay.DrawTime += ModColors(ApplyOpacity);

        // Chapter-mode: bg.Draw(Vector2) → internally calls get_White().
        // Hook it with a guarded opacity so only timer rendering is affected.
        var mtexDraw1 = typeof(MTexture).GetMethod("Draw", [typeof(Vector2)]);
        if (mtexDraw1 != null)
            _mtexDrawHook = new ILHook(mtexDraw1, ModColors(GuardedOpacity));

        On.Celeste.SpeedrunTimerDisplay.Render += OnRender;
    }

    public override void Unload() {
        IL.Celeste.SpeedrunTimerDisplay.Render -= ModColors(ApplyOpacity);
        IL.Celeste.SpeedrunTimerDisplay.DrawTime -= ModColors(ApplyOpacity);
        _mtexDrawHook?.Dispose();
        On.Celeste.SpeedrunTimerDisplay.Render -= OnRender;
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

    // ── IL hook factory ────────────────────────────────────────────────

    private static ILContext.Manipulator ModColors(Func<Color, Color> opacity) => il => {
        var cursor = new ILCursor(il);
        var positions = new List<int>();
        while (cursor.TryGotoNext(MoveType.After, IsColorProducer))
            positions.Add(cursor.Index);
        for (int i = positions.Count - 1; i >= 0; i--) {
            cursor.Index = positions[i];
            cursor.EmitDelegate(opacity);
        }
    };

    private static bool IsColorProducer(Instruction instr) {
        if ((instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt)
            && instr.Operand is MethodReference m
            && m.DeclaringType.FullName == "Microsoft.Xna.Framework.Color"
            && m.ReturnType.FullName == "Microsoft.Xna.Framework.Color"
            && m.Name != "op_Multiply")
            return true;
        if (instr.OpCode == OpCodes.Newobj
            && instr.Operand is MethodReference ctor
            && ctor.DeclaringType.FullName == "Microsoft.Xna.Framework.Color")
            return true;
        if ((instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt)
            && instr.Operand is MethodReference hex
            && hex.DeclaringType.FullName == "Monocle.Calc"
            && hex.Name == "HexToColor")
            return true;
        if ((instr.OpCode == OpCodes.Ldsfld || instr.OpCode == OpCodes.Ldfld)
            && instr.Operand is FieldReference f
            && f.FieldType.FullName == "Microsoft.Xna.Framework.Color")
            return true;
        if (instr.OpCode == OpCodes.Ldobj
            && instr.Operand is TypeReference t
            && t.FullName == "Microsoft.Xna.Framework.Color")
            return true;
        return false;
    }

    // ── Opacity functions ──────────────────────────────────────────────

    private static Color ApplyOpacity(Color c) {
        if (!Settings.Enabled) return c;
        return c * (Settings.Opacity / 10f);
    }

    private static Color GuardedOpacity(Color c) {
        if (!Settings.Enabled || !_isRenderingTimer) return c;
        return c * (Settings.Opacity / 10f);
    }

    // ── On hook ────────────────────────────────────────────────────────

    private static void OnRender(On.Celeste.SpeedrunTimerDisplay.orig_Render orig, SpeedrunTimerDisplay self) {
        if (!Settings.Enabled) { orig(self); return; }
        _isRenderingTimer = true;
        orig(self);
        _isRenderingTimer = false;
    }
}
