using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PadToMIDI.Core.Input;

namespace PadToMIDI.App.Controls;

/// <summary>Original generic artwork. Observes a value snapshot; never processes musical input.</summary>
public sealed class GamepadMonitor : Control
{
    public static readonly StyledProperty<GamepadValues> ValuesProperty =
        AvaloniaProperty.Register<GamepadMonitor, GamepadValues>(nameof(Values));
    private static readonly IBrush Body = new SolidColorBrush(Color.Parse("#24334B"));
    private static readonly IBrush Idle = new SolidColorBrush(Color.Parse("#0F1A2B"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#65DFC0"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#9AAAC1"));
    private static readonly Pen Outline = new(new SolidColorBrush(Color.Parse("#43546D")), 1.5);
    private static readonly Geometry BodyShape = Geometry.Parse(
        "M 120,45 L 420,45 C 455,45 470,65 485,110 L 515,190 C 529,225 502,240 477,217 L 424,174 L 116,174 L 63,217 C 38,240 11,225 25,190 L 55,110 C 70,65 85,45 120,45 Z");

    static GamepadMonitor() => AffectsRender<GamepadMonitor>(ValuesProperty);

    public GamepadValues Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double scale = Math.Min(Bounds.Width / 540, Bounds.Height / 255);
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation((Bounds.Width - 540 * scale) / 2, (Bounds.Height - 255 * scale) / 2));
        context.DrawGeometry(Body, Outline, BodyShape);

        DrawButton(context, PhysicalControl.LeftBumper, new(95, 24, 105, 16), "LB");
        DrawButton(context, PhysicalControl.RightBumper, new(340, 24, 105, 16), "RB");
        DrawButton(context, PhysicalControl.DPadUp, new(102, 71, 28, 28), "↑");
        DrawButton(context, PhysicalControl.DPadDown, new(102, 129, 28, 28), "↓");
        DrawButton(context, PhysicalControl.DPadLeft, new(73, 100, 28, 28), "←");
        DrawButton(context, PhysicalControl.DPadRight, new(131, 100, 28, 28), "→");
        DrawButton(context, PhysicalControl.FaceNorth, new(407, 71, 28, 28), "N");
        DrawButton(context, PhysicalControl.FaceSouth, new(407, 129, 28, 28), "S");
        DrawButton(context, PhysicalControl.FaceWest, new(378, 100, 28, 28), "W");
        DrawButton(context, PhysicalControl.FaceEast, new(436, 100, 28, 28), "E");
        DrawButton(context, PhysicalControl.Back, new(216, 85, 28, 18), "−");
        DrawButton(context, PhysicalControl.Start, new(296, 85, 28, 18), "+");
        DrawButton(context, PhysicalControl.Guide, new(256, 79, 28, 28), "•");
        DrawStick(context, new(194, 151), Values.LeftStickX, Values.LeftStickY, Values.IsPressed(PhysicalControl.LeftStickButton));
        DrawStick(context, new(346, 151), Values.RightStickX, Values.RightStickY, Values.IsPressed(PhysicalControl.RightStickButton));
    }

    private void DrawButton(DrawingContext context, PhysicalControl control, Rect rect, string text)
    {
        bool pressed = Values.IsPressed(control);
        context.DrawRectangle(pressed ? Accent : Idle, Outline, rect, 7, 7);
        var label = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, pressed ? Idle : Muted);
        context.DrawText(label, new(rect.Center.X - label.Width / 2, rect.Center.Y - label.Height / 2));
    }

    private static void DrawStick(DrawingContext context, Point center, float x, float y, bool pressed)
    {
        context.DrawEllipse(Idle, Outline, center, 28, 28);
        context.DrawLine(Outline, center - new Vector(24, 0), center + new Vector(24, 0));
        context.DrawLine(Outline, center - new Vector(0, 24), center + new Vector(0, 24));
        context.DrawEllipse(pressed ? Accent : Muted, null, center + new Vector(x * 20, y * 20), 9, 9);
    }
}
