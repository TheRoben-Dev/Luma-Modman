using Godot;

namespace Luma.App.Ui;

/// <summary>Toggle row ("Caption   On/Off") built on Stepper so it behaves the same.</summary>
public static class Toggle
{
    /// <summary>With onDetail/offDetail the row expands to explain the current value while focused.</summary>
    public static Stepper Create(string caption, bool value, Action<bool> changed, string help = "", string? onDetail = null, string? offDetail = null)
    {
        var s = new Stepper(caption, [("true", "On"), ("false", "Off")], value ? "true" : "false", help)
        {
            Expands = onDetail != null || offDetail != null,
            DetailFor = v => v == "true" ? onDetail : offDetail,
        };
        s.Changed += v => changed(v == "true");
        return s;
    }
}
