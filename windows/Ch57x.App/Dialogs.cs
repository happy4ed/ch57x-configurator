using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>
/// 작은 대화상자들 (Fluent ContentDialog 모양 · 안쪽 여백 24 · 강조 단추 하나 · Enter = 주 단추 · Esc = 취소).
/// 확인 · 이름 입력 · 키/노브 개수 · 레이어 복사 대상 · 오류(판정 S14 · 첫 사용 점검 1번: 쉬운 말 한 줄 + 할 일 · 「다시 해 보기」 · 「정보 복사」).
/// </summary>
public sealed class DialogWindow : FluentWindow
{
    public StackPanel Body { get; } = new();
    public Button Primary { get; } = new() { MinWidth = 120, IsDefault = true };
    public Button Secondary { get; } = new() { MinWidth = 120, IsCancel = true };
    public StackPanel FooterLeft { get; } = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    public Func<bool>? Validate { get; set; }

    public DialogWindow(Window? owner, string title, double width = 440)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = owner == null;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        if (owner != null && owner.IsVisible) Owner = owner;

        Primary.SetResourceReference(StyleProperty, "AccentButton");
        Primary.Click += (_, _) => { if (Validate == null || Validate()) DialogResult = true; };

        var footerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        footerButtons.Children.Add(Primary);
        Secondary.Margin = new Thickness(8, 0, 0, 0);
        footerButtons.Children.Add(Secondary);
        var footerDock = new DockPanel();
        DockPanel.SetDock(footerButtons, Dock.Right);
        footerDock.Children.Add(footerButtons);
        footerDock.Children.Add(FooterLeft);
        var footer = new Border { Padding = new Thickness(24), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerDock };
        footer.SetResourceReference(Border.BackgroundProperty, "Bg");
        footer.SetResourceReference(Border.BorderBrushProperty, "CardStroke");

        var body = new Border { Padding = new Thickness(24), Child = Body };
        body.SetResourceReference(Border.BackgroundProperty, "Bg3");
        var dock = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(body);
        Content = dock;
    }

    public static TextBlock Text(string s, string style = "", double top = 0)
    {
        var t = new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
        if (style.Length > 0) t.SetResourceReference(StyleProperty, style);
        return t;
    }
}

public static class Dialogs
{
    private static Window? OwnerOf(Window? w) => w != null && w.IsVisible ? w : null;

    /// <summary>예 / 아니오. danger = 주 단추가 지우기 같은 일</summary>
    public static bool Confirm(Window? owner, string title, string message, string primary, string secondary = "취소")
    {
        var d = new DialogWindow(OwnerOf(owner), title);
        d.Body.Children.Add(DialogWindow.Text(message));
        d.Primary.Content = primary;
        d.Secondary.Content = secondary;
        return d.ShowDialog() == true;
    }

    /// <summary>이름 같은 글 하나. validate 가 문제 글을 돌려주면 창을 닫지 않고 그 글을 빨갛게 보인다.</summary>
    public static string? Input(Window? owner, string title, string label, string initial, Func<string, string?> validate, string primary = "확인", string? hint = null)
    {
        var d = new DialogWindow(OwnerOf(owner), title);
        d.Body.Children.Add(DialogWindow.Text(label, "FieldHeader"));
        var box = new TextBox { Text = initial, MaxLength = ProfileNames.MaxLength };
        d.Body.Children.Add(box);
        var err = DialogWindow.Text("", "", 8);
        err.SetResourceReference(TextBlock.ForegroundProperty, "Critical");
        err.Visibility = Visibility.Collapsed;
        d.Body.Children.Add(err);
        if (hint != null) d.Body.Children.Add(DialogWindow.Text(hint, "Caption", 8));
        d.Primary.Content = primary;
        d.Secondary.Content = "취소";
        d.Validate = () =>
        {
            var p = validate(box.Text);
            err.Text = p ?? "";
            err.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
            return p == null;
        };
        d.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return d.ShowDialog() == true ? box.Text.Trim() : null;
    }

    /// <summary>「키 · 노브 개수…」 — 연결하면 자동으로 맞추지만(지원 펌웨어) 손으로도</summary>
    public static (int Keys, int Knobs)? Counts(Window? owner, int keys, int knobs)
    {
        var d = new DialogWindow(OwnerOf(owner), "키 · 노브 개수");
        var k = new ComboBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        for (int i = 1; i <= 15; i++) k.Items.Add(i);
        k.SelectedItem = Math.Clamp(keys, 1, 15);
        var n = new ComboBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        for (int i = 0; i <= 3; i++) n.Items.Add(i);
        n.SelectedItem = Math.Clamp(knobs, 0, 3);
        d.Body.Children.Add(DialogWindow.Text("키 개수", "FieldHeader"));
        d.Body.Children.Add(k);
        d.Body.Children.Add(DialogWindow.Text("노브 개수", "FieldHeader", 16));
        d.Body.Children.Add(n);
        d.Body.Children.Add(DialogWindow.Text("키보드를 연결하면 자동으로 맞춤(개수를 알려 주는 펌웨어만).", "Caption", 16));
        d.Primary.Content = Terms.Apply;
        d.Secondary.Content = "취소";
        return d.ShowDialog() == true ? ((int)k.SelectedItem, (int)n.SelectedItem) : null;
    }

    /// <summary>「다른 레이어로 복사…」 대상 고르기</summary>
    public static int? LayerTarget(Window? owner, int from)
    {
        var d = new DialogWindow(OwnerOf(owner), $"레이어 {from + 1} 복사");
        d.Body.Children.Add(DialogWindow.Text("복사할 곳", "FieldHeader"));
        var radios = new List<RadioButton>();
        for (int l = 0; l < 3; l++)
        {
            if (l == from) continue;
            var r = new RadioButton { Content = $"레이어 {l + 1}", Tag = l, GroupName = "to", IsChecked = radios.Count == 0 };
            radios.Add(r);
            d.Body.Children.Add(r);
        }
        d.Body.Children.Add(DialogWindow.Text($"레이어 {from + 1} 의 키를 그대로 옮겨 적음 — 그 레이어의 키는 덮어씀 · 조명은 그대로 · 실행 취소로 되돌림", "Caption", 12));
        d.Primary.Content = "복사";
        d.Secondary.Content = "취소";
        return d.ShowDialog() == true ? (int?)radios.First(r => r.IsChecked == true).Tag : null;
    }

    /// <summary>
    /// 오류 알림 (판정 S14 · 첫 사용 점검 1번). 본문 = 쉬운 말 한 줄 + 할 일(Core ErrorText) — 예외 글 같은 기술 말은 「정보 복사」 내용에만.
    /// retry 가 있으면(업로드 · 읽기처럼 다시 할 수 있는 일) 주 단추 「다시 해 보기」 · 보조 「닫기」, 없으면 「닫기」 하나.
    /// context = 무슨 일을 하다 났는지(정보 복사의 「상황」). device = 키보드와 주고받던 일.
    /// </summary>
    public static void Error(Window? owner, string context, Exception ex, Action? retry = null, bool device = false)
    {
        bool again;
        try { again = BuildError(owner, context, ex, retry != null, device).ShowDialog() == true && retry != null; }
        catch (Exception inner) { Log.Error("오류 창", inner); return; }
        if (again) retry!();
    }

    public static DialogWindow BuildError(Window? owner, string context, Exception ex, bool canRetry = false, bool device = false)
    {
        var d = new DialogWindow(OwnerOf(owner), "오류");
        var plain = ErrorText.Of(ex, device);
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap };
        line.Inlines.Add(new Run(plain.What) { FontWeight = FontWeights.SemiBold });
        line.Inlines.Add(new Run($" — {plain.Todo}"));
        d.Body.Children.Add(line);
        var copy = new Button { Content = "정보 복사", Tag = "copy", ToolTip = "알려 주실 때 붙여 넣을 자세한 내용(판 · 오류 · 최근 로그)을 복사" };
        copy.Click += (_, _) => { if (Clip(ErrorReport.Build(context, ex))) copy.Content = "복사됨"; };
        d.FooterLeft.Children.Add(copy);
        if (canRetry)
        {
            d.Primary.Content = "다시 해 보기";
            d.Secondary.Content = "닫기";
        }
        else
        {
            d.Primary.Content = "닫기";
            d.Primary.IsCancel = true;
            d.Secondary.Visibility = Visibility.Collapsed;
        }
        return d;
    }

    public static bool Clip(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch (Exception ex) { Log.Error("복사", ex); return false; }
    }
}

/// <summary>「정보 복사」 글 — 판 · OS · 오류 전문 · 최근 로그 (판정 S14 · A3)</summary>
public static class ErrorReport
{
    public static string Version()
    {
        var asm = Assembly.GetExecutingAssembly();
        var v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "";
        var plus = v.IndexOf('+');
        return plus > 0 ? v[..plus] : v;
    }

    public static string Environment_() =>
        $"{Terms.AppName} {Version()} · OS {Environment.OSVersion} · .NET {Environment.Version}";

    public static string Build(string context, Exception ex)
    {
        var lines = Log.Lines.Skip(Math.Max(0, Log.Lines.Count - 30));
        return string.Join(Environment.NewLine, new[]
        {
            Environment_(),
            $"시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"상황: {context}",
            "",
            ex.ToString(),
            "",
            "최근 로그:",
        }.Concat(lines));
    }
}
