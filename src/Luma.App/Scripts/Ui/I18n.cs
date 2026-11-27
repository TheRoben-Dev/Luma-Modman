using Godot;
using Luma.Host;

namespace Luma.App.Ui;

/// <summary>
/// Puts Luma's language into Godot's translation system. Every label, button, tooltip and menu
/// item is translated as it's shown (Godot's auto-translate), so screens need no changes: text
/// with values in it is matched by <see cref="Translator"/>'s {0}-style keys. Switching language
/// takes effect at once.
/// </summary>
public static partial class I18n
{
    private static LumaTranslation? _current;

    public static string Code { get; private set; } = "en";

    public static void Use(string? setting)
    {
        var code = Translations.Pick(setting);
        if (_current != null)
        {
            TranslationServer.RemoveTranslation(_current);
            _current = null;
        }
        Code = code;
        if (Translations.For(code) is { } t)
        {
            _current = new LumaTranslation(t) { Locale = code };
            TranslationServer.AddTranslation(_current);
        }
        TranslationServer.SetLocale(code);
        AppLog.Info($"language {code}" + (_current != null ? $" ({_current.Translator.Count} strings)" : ""));
    }

    /// <summary>Text for places Godot doesn't translate by itself (window titles, drawn text).</summary>
    public static string T(string text) => _current?.Translator.Translate(text) ?? text;

    private sealed partial class LumaTranslation : Translation
    {
        public Translator Translator { get; }

        public LumaTranslation(Translator translator) => Translator = translator;

        public LumaTranslation() => Translator = new Translator(new());

        public override StringName _GetMessage(StringName srcMessage, StringName context) =>
            Translator.Translate(srcMessage.ToString()) is { } s ? s : srcMessage;
    }
}
