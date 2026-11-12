using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Ch57x.App;

/// <summary>
/// 아이콘 (판정 E5 「기호로 적은건 크기 이슈, 아이콘을 다시 만들어줘」) — 2판 개선안 아이콘(artifacts/ch57x-pc-app-ux/icons/*.svg ·
/// 생성기 projects/ch57x/tools/pc-app-ux/build3.py ICONS · PARTS)을 그대로 옮긴 벡터. 20 격자 · 선 1.6 · 둥근 끝.
/// 색은 글자색(TextElement.Foreground)을 따른다 — 라이트 · 다크 · 단추 상태가 저절로 맞는다. 16 · 20 · 24 어디서나 벡터로 그린다.
/// </summary>
public sealed class Glyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Glyph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Glyph), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Glyph), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public string? Kind { get => (string?)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public Glyph() { SnapsToDevicePixels = false; Focusable = false; IsHitTestVisible = false; VerticalAlignment = VerticalAlignment.Center; }

    public static Glyph Of(string kind, double size = 16) => new() { Kind = kind, Size = size, VerticalAlignment = VerticalAlignment.Center };

    protected override System.Windows.Size MeasureOverride(System.Windows.Size available) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        var parts = GlyphData.Get(Kind);
        if (parts == null) return;
        var brush = Foreground;
        double s = Size / 20.0;
        dc.PushTransform(new ScaleTransform(s, s));
        var pen = new Pen(brush, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        foreach (var (geo, fill) in parts) dc.DrawGeometry(fill ? brush : null, fill ? null : pen, geo);
        dc.Pop();
    }
}

/// <summary>아이콘 모양 (20×20 격자). (도형, 채움 여부) — 채움이 아니면 선 1.6.</summary>
public static class GlyphData
{
    private static readonly Dictionary<string, List<(Geometry, bool)>> Cache = new();

    private static Geometry P(string d) => Geometry.Parse(d);
    private static Geometry C(double cx, double cy, double r) => new EllipseGeometry(new Point(cx, cy), r, r);
    private static Geometry R(double x, double y, double w, double h, double rx) => new RectangleGeometry(new Rect(x, y, w, h), rx, rx);
    private static Geometry T(Geometry g, Transform t) { var c = g.Clone(); c.Transform = t; return c; }

    private static List<(Geometry, bool)>? Build(string kind)
    {
        // 노브 돌림: 에이전트 패드 키 배치 그림(keymap.svg)의 호 + 삼각 화살 — 오른쪽은 좌우 반전 짝
        var arc = "M10 4.5 A6.5 6.5 0 1 1 3.5 11";
        var head = "M4.5 4.5 L10.5 0.5 L10.5 8.5 Z";
        Transform ccw = new TranslateTransform(0, 1);
        Transform cw = new MatrixTransform(-1, 0, 0, 1, 20, 1);
        return kind switch
        {
            "knob-ccw" => new() { (T(P(arc), ccw), false), (T(P(head), ccw), true) },
            "knob-cw" => new() { (T(P(arc), cw), false), (T(P(head), cw), true) },
            "knob-press" => new() { (C(10, 10, 6.5), false), (C(10, 10, 2.5), true) },
            "knob" => new() { (C(10, 10, 7), false), (P("M10 10 L6.6 6.6"), false), (C(10, 10, 1.2), true) },
            "layer" => new() { (P("M10 3 L17 6.8 L10 10.6 L3 6.8 Z"), false), (P("M3 10.4 L10 14.2 L17 10.4"), false), (P("M3 13.8 L10 17.6 L17 13.8"), false) },
            "upload" => new() { (R(2.8, 11.2, 14.4, 6.6, 1.8), false), (P("M10 2.2 V8.4 M7.2 5.8 L10 8.6 L12.8 5.8"), false) },
            "read" => new() { (R(2.8, 11.2, 14.4, 6.6, 1.8), false), (P("M10 8.6 V2.4 M7.2 5 L10 2.2 L12.8 5"), false) },
            "pc-load" => new() { (R(2.6, 3, 14.8, 10, 1.6), false), (P("M7.4 17.2 H12.6 M10 13 V17.2"), false), (P("M10 5.4 V10 M8 8 L10 10 L12 8"), false) },
            "undo" => new() { (P("M7 4.6 L3.6 8 L7 11.4"), false), (P("M3.8 8 H12 A4.4 4.4 0 0 1 12 16.8 H8.6"), false) },
            "led" => new() { (R(6.4, 6.4, 7.2, 7.2, 1.6), false), (P("M10 1.8 V3.8 M10 16.2 V18.2 M1.8 10 H3.8 M16.2 10 H18.2 M4.2 4.2 L5.6 5.6 M14.4 14.4 L15.8 15.8 M15.8 4.2 L14.4 5.6 M5.6 14.4 L4.2 15.8"), false) },
            "lock" => new() { (R(4.4, 9, 11.2, 8.6, 2), false), (P("M7 9 V6.6 A3 3 0 0 1 13 6.6 V9"), false) },
            "unlock" => new() { (R(4.4, 9, 11.2, 8.6, 2), false), (P("M7 9 V6.6 A3 3 0 0 1 12.8 5.4"), false) },
            "grip" => new() { (C(7.4, 5, 1.3), true), (C(12.6, 5, 1.3), true), (C(7.4, 10, 1.3), true), (C(12.6, 10, 1.3), true), (C(7.4, 15, 1.3), true), (C(12.6, 15, 1.3), true) },
            "more" => new() { (C(4.6, 10, 1.4), true), (C(10, 10, 1.4), true), (C(15.4, 10, 1.4), true) },
            "keyboard" => new() { (R(2.4, 5, 15.2, 10, 1.8), false), (P("M5.6 8.2 H5.7 M8.6 8.2 H8.7 M11.4 8.2 H11.5 M14.4 8.2 H14.5 M7 11.8 H13"), false) },
            "chev" => new() { (P("M5.6 7.8 L10 12.2 L14.4 7.8"), false) },
            "chev-up" => new() { (P("M5.6 12.2 L10 7.8 L14.4 12.2"), false) },
            "chev-r" => new() { (P("M7.8 5.6 L12.2 10 L7.8 14.4"), false) },
            "check" => new() { (P("M4.8 10.4 L8.4 14 L15.2 6.4"), false) },
            "x" => new() { (P("M5.5 5.5 L14.5 14.5 M14.5 5.5 L5.5 14.5"), false) },
            "min" => new() { (P("M5 10 H15"), false) },
            "plus" => new() { (P("M10 4.5 V15.5 M4.5 10 H15.5"), false) },
            "copy" => new() { (R(6.6, 6.6, 10, 11, 1.8), false), (P("M4 13.4 V4.8 A1.8 1.8 0 0 1 5.8 3 H12.4"), false) },
            "trash" => new() { (P("M3.6 5.4 H16.4 M8 5.4 V3.6 H12 V5.4 M5.4 5.4 L6.2 17 H13.8 L14.6 5.4"), false) },
            "info" => new() { (C(10, 10, 7.4), false), (P("M10 9 V14"), false), (C(10, 6.4, 1.1), true) },
            "error" => new() { (C(10, 10, 7.4), false), (P("M10 5.8 V11"), false), (C(10, 13.8, 1.1), true) },
            "theme" => new() { (C(10, 10, 7.4), false), (P("M10 2.6 A7.4 7.4 0 0 1 10 17.4 Z"), true) },
            "ok" => new() { (C(10, 10, 7.4), false), (P("M6.8 10.2 L9 12.4 L13.4 7.8"), false) },
            "dot" => new() { (C(10, 10, 3), true) },
            _ => null,
        };
    }

    public static IReadOnlyList<(Geometry Geometry, bool Fill)>? Get(string? kind)
    {
        if (string.IsNullOrEmpty(kind)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(kind, out var hit)) return hit;
            var built = Build(kind);
            if (built == null) return null;
            for (int i = 0; i < built.Count; i++) built[i].Item1.Freeze();
            Cache[kind] = built;
            return built;
        }
    }
}
