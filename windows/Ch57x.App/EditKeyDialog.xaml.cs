using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ch57x.Core;

namespace Ch57x.App;

public partial class EditKeyDialog : Window
{
    private static readonly (string V, string Label)[] Types =
    {
        ("none", "없음"), ("key", "⌨ 키보드"), ("text", "📝 상용구"), ("media", "🎵 미디어"), ("mouse", "🖱 마우스"),
    };
    private string _type;
    public Binding? Result { get; private set; }
    private readonly Binding _initial;

    // editing state — keyboard supports up to 18 sequential steps
    private List<Accord> _steps = new() { new Accord() };
    private int _delay;
    private string _text = "";
    private string _media = "VolumeUp";
    private string _mouseAct = "click";
    private List<string> _mouseMods = new();
    private List<string> _mouseBtns = new();
    private int _dx, _dy;
    private int _delta = 1;

    public EditKeyDialog(string title, int layer, Binding? current)
    {
        InitializeComponent();
        TitleText.Text = $"{title}  ·  레이어 {layer}";
        _initial = current ?? new Binding { Type = BindingType.None };
        TxtAlias.Text = _initial.Alias ?? "";

        // load editing state from current binding
        _type = _initial.Type switch { BindingType.Key => "key", BindingType.Text => "text", BindingType.Media => "media", BindingType.Mouse => "mouse", _ => "none" };
        if (_initial.Type == BindingType.Key && _initial.Steps?.Count > 0)
        {
            _steps = _initial.Steps.Select(s => new Accord { Mods = s.Mods?.ToList() ?? new(), Code = s.Code }).ToList();
            _delay = _initial.Delay;
        }
        else if (_initial.Type == BindingType.Text) { _text = _initial.Text ?? ""; _delay = _initial.Delay; }
        else if (_initial.Type == BindingType.Media) _media = _initial.Media ?? "VolumeUp";
        else if (_initial.Type == BindingType.Mouse)
        {
            _mouseAct = _initial.Action ?? "click";
            _mouseMods = _initial.Mods?.ToList() ?? new();
            _mouseBtns = _initial.Buttons?.ToList() ?? new();
            _dx = _initial.Dx; _dy = _initial.Dy; _delta = _initial.Delta == 0 ? 1 : _initial.Delta;
        }

        BuildTypeBtns();
        RenderBody();
        BtnOk.Click += (_, _) => { Result = BuildResult(); DialogResult = true; };
        BtnCancel.Click += (_, _) => { DialogResult = false; };
        BtnClear.Click += (_, _) => { Result = new Binding { Type = BindingType.None }; DialogResult = true; };
    }

    private void BuildTypeBtns()
    {
        TypeBtns.Children.Clear();
        foreach (var (v, label) in Types)
        {
            string vv = v;
            var b = new Button
            {
                Content = label, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 6),
                Background = v == _type ? new SolidColorBrush(Color.FromRgb(0x2d, 0x6c, 0xdf)) : new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2e)),
                Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2f, 0x3a)),
                BorderThickness = new Thickness(1),
            };
            b.Click += (_, _) => { _type = vv; BuildTypeBtns(); RenderBody(); };
            TypeBtns.Children.Add(b);
        }
    }

    private void RenderBody()
    {
        Body.Children.Clear();
        if (_type == "none") { Body.Children.Add(Hint("이 키는 비어 있습니다.")); return; }
        if (_type == "key") RenderKeyBody();
        else if (_type == "text") RenderTextBody();
        else if (_type == "media") RenderMediaBody();
        else if (_type == "mouse") RenderMouseBody();
    }

    private void RenderKeyBody()
    {
        if (_steps.Count == 0) _steps.Add(new Accord());
        bool removable = _steps.Count > 1;
        for (int i = 0; i < _steps.Count; i++) Body.Children.Add(StepRow(i, removable));

        if (_steps.Count < 18)
        {
            var add = new Button { Content = "+ 단계 추가", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 4, 0, 10),
                Background = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2e)), Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2f, 0x3a)), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (_, _) => { _steps.Add(new Accord()); RenderBody(); };
            Body.Children.Add(add);
        }

        // delay
        var delayRow = new StackPanel { Orientation = Orientation.Horizontal };
        delayRow.Children.Add(new TextBlock { Text = "지연(ms, 0=없음)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)) });
        var dly = new TextBox { Width = 80, Text = _delay.ToString(), Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2e)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2f, 0x3a)) };
        dly.TextChanged += (_, _) => int.TryParse(dly.Text, out _delay);
        delayRow.Children.Add(dly);
        Body.Children.Add(delayRow);
        Body.Children.Add(Hint("여러 단계는 순서대로 입력됩니다 (최대 18). Ctrl+W 같은 브라우저 가로채기는 기본 키만 입력 후 수정자를 토글 버튼으로 켜세요."));
    }

    private FrameworkElement StepRow(int idx, bool removable)
    {
        var s = _steps[idx];
        var outer = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2f, 0x3a)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8),
        };
        var sp = new StackPanel();

        // top: step number + modifier toggles + delete
        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        if (removable)
        {
            topRow.Children.Add(new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x6c, 0xdf)),
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock { Text = (idx + 1).ToString(), Foreground = Brushes.White, FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        }
        foreach (var m in new[] { "Ctrl", "Shift", "Alt", "Win" })
        {
            string mm = m;
            topRow.Children.Add(MakeToggle(m, s.Mods?.Contains(m) == true, () =>
            {
                s.Mods ??= new();
                if (s.Mods.Contains(mm)) s.Mods.Remove(mm); else s.Mods.Add(mm);
                RenderBody();
            }));
        }
        if (removable)
        {
            var del = new Button { Content = "✕", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2e)), Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2f, 0x3a)), BorderThickness = new Thickness(1) };
            del.Click += (_, _) => { _steps.RemoveAt(idx); if (_steps.Count == 0) _steps.Add(new Accord()); RenderBody(); };
            topRow.Children.Add(del);
        }
        sp.Children.Add(topRow);

        // bottom: key dropdown + capture
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal };
        var combo = new ComboBox { Width = 240, Margin = new Thickness(0, 0, 8, 0) };
        combo.Items.Add(new ComboBoxItem { Content = "(없음)", Tag = "" });
        foreach (var (name, label) in KeyCodes.Order)
        {
            var it = new ComboBoxItem { Content = $"{label}  ({name})", Tag = name };
            combo.Items.Add(it);
            if (name == s.Code) combo.SelectedItem = it;
        }
        if (combo.SelectedItem == null) combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) => s.Code = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
        keyRow.Children.Add(combo);

        var capture = new TextBox
        {
            Width = 180, Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2e)),
            Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2f, 0x3a)),
            IsReadOnly = true, Text = "여기 클릭 후 키 누르기", TextAlignment = TextAlignment.Center,
        };
        capture.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
            var name = WpfKeyToName(e.Key);
            if (name == null) return;
            s.Code = name;
            s.Mods = new();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) s.Mods.Add("Ctrl");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) s.Mods.Add("Shift");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) s.Mods.Add("Alt");
            if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) s.Mods.Add("Win");
            RenderBody();
        };
        keyRow.Children.Add(capture);
        sp.Children.Add(keyRow);

        outer.Child = sp;
        return outer;
    }

    private void RenderTextBody()
    {
        Body.Children.Add(new TextBlock { Text = "텍스트 (상용구)", Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)), Margin = new Thickness(0, 0, 0, 4) });
        var tb = new TextBox { Text = _text, MaxLength = 40, TextWrapping = TextWrapping.Wrap, MinHeight = 50,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2e)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2f, 0x3a)) };
        tb.TextChanged += (_, _) => _text = tb.Text;
        Body.Children.Add(tb);
        Body.Children.Add(Hint("문자를 키 시퀀스로 자동 변환 (US 배열, 최대 18자). 한글/IME 불가."));
    }

    private void RenderMediaBody()
    {
        Body.Children.Add(new TextBlock { Text = "미디어 키", Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)), Margin = new Thickness(0, 0, 0, 4) });
        var combo = new ComboBox();
        foreach (var (name, (code, label)) in KeyCodes.MediaCodes)
        {
            var it = new ComboBoxItem { Content = $"{label} ({name})", Tag = name };
            combo.Items.Add(it);
            if (name == _media) combo.SelectedItem = it;
        }
        if (combo.SelectedItem == null) combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) => _media = (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "VolumeUp";
        Body.Children.Add(combo);
    }

    private void RenderMouseBody()
    {
        // action picker
        var actCombo = new ComboBox { Width = 160, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (v, label) in new[] { ("click", "클릭"), ("wheel", "휠"), ("move", "이동"), ("drag", "드래그") })
        {
            var it = new ComboBoxItem { Content = label, Tag = v }; actCombo.Items.Add(it);
            if (v == _mouseAct) actCombo.SelectedItem = it;
        }
        actCombo.SelectionChanged += (_, _) => { _mouseAct = (actCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "click"; RenderBody(); };
        Body.Children.Add(Row("동작", actCombo));

        // mouse modifiers
        var modRow = new WrapPanel();
        foreach (var m in new[] { "Ctrl", "Shift", "Alt" })
        {
            string mm = m;
            var btn = MakeToggle(m, _mouseMods.Contains(m), () =>
            { if (_mouseMods.Contains(mm)) _mouseMods.Remove(mm); else _mouseMods.Add(mm); RenderBody(); });
            modRow.Children.Add(btn);
        }
        Body.Children.Add(Row("수정자", modRow));

        if (_mouseAct == "click" || _mouseAct == "drag")
        {
            var btnRow = new WrapPanel();
            foreach (var (m, label) in new[] { ("Left", "왼쪽"), ("Right", "오른쪽"), ("Middle", "가운데") })
            {
                string mm = m;
                var b = MakeToggle(label, _mouseBtns.Contains(m), () =>
                { if (_mouseBtns.Contains(mm)) _mouseBtns.Remove(mm); else _mouseBtns.Add(mm); RenderBody(); });
                btnRow.Children.Add(b);
            }
            Body.Children.Add(Row("버튼", btnRow));
        }
        if (_mouseAct == "move" || _mouseAct == "drag")
        {
            var dxRow = new StackPanel { Orientation = Orientation.Horizontal };
            var dx = NumBox(_dx, v => _dx = v); var dy = NumBox(_dy, v => _dy = v);
            dxRow.Children.Add(new TextBlock { Text = "dx", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            dxRow.Children.Add(dx);
            dxRow.Children.Add(new TextBlock { Text = "  dy", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
            dxRow.Children.Add(dy);
            Body.Children.Add(Row("이동", dxRow));
        }
        if (_mouseAct == "wheel")
        {
            var up = MakeToggle("위", _delta >= 0, () => { _delta = 1; RenderBody(); });
            var dn = MakeToggle("아래", _delta < 0, () => { _delta = -1; RenderBody(); });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(up); row.Children.Add(dn);
            Body.Children.Add(Row("방향", row));
        }
    }

    private TextBox NumBox(int initial, Action<int> on)
    {
        var t = new TextBox { Width = 70, Text = initial.ToString(), Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2e)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2f, 0x3a)) };
        t.TextChanged += (_, _) => { if (int.TryParse(t.Text, out var v)) on(v); };
        return t;
    }
    private Button MakeToggle(string label, bool on, Action onClick)
    {
        var b = new Button
        {
            Content = label, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 0),
            Background = on ? new SolidColorBrush(Color.FromRgb(0x2d, 0x6c, 0xdf)) : new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2e)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2f, 0x3a)),
            BorderThickness = new Thickness(1),
        };
        b.Click += (_, _) => onClick();
        return b;
    }
    private FrameworkElement Row(string label, FrameworkElement field)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)), Margin = new Thickness(0, 0, 0, 4) });
        sp.Children.Add(field);
        return sp;
    }
    private FrameworkElement Hint(string s) => new TextBlock { Text = s, Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x93, 0xa1)), FontSize = 13, Margin = new Thickness(0, 4, 0, 0) };

    private Binding? BuildResult()
    {
        string alias = TxtAlias.Text.Trim();
        Binding? b = _type switch
        {
            "key" => (() => {
                var steps = _steps.Where(s => (s.Mods?.Count ?? 0) > 0 || !string.IsNullOrEmpty(s.Code))
                    .Select(s => new Accord { Mods = s.Mods?.ToList() ?? new(), Code = s.Code }).ToList();
                return steps.Count == 0
                    ? new Binding { Type = BindingType.None }
                    : new Binding { Type = BindingType.Key, Steps = steps, Delay = _delay };
            })(),
            "text" => string.IsNullOrEmpty(_text)
                ? new Binding { Type = BindingType.None }
                : new Binding { Type = BindingType.Text, Text = _text, Delay = _delay },
            "media" => new Binding { Type = BindingType.Media, Media = _media },
            "mouse" => new Binding
            {
                Type = BindingType.Mouse,
                Action = _mouseAct,
                Mods = _mouseMods.ToList(),
                Buttons = _mouseBtns.ToList(),
                Dx = _dx, Dy = _dy,
                Delta = _mouseAct == "wheel" ? _delta : 0,
            },
            _ => new Binding { Type = BindingType.None },
        };
        if (b != null && b.Type != BindingType.None && !string.IsNullOrEmpty(alias)) b.Alias = alias;
        return b;
    }

    /// <summary>Map WPF Key enum to our keycode names (limited subset that covers the common cases).</summary>
    private static string? WpfKeyToName(Key k)
    {
        // letters
        if (k >= Key.A && k <= Key.Z) return ((char)('A' + (k - Key.A))).ToString();
        // top digits
        if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
        // numpad
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return "NumPad" + (k - Key.NumPad0);
        // function
        if (k >= Key.F1 && k <= Key.F24) return "F" + (k - Key.F1 + 1);
        return k switch
        {
            Key.Enter => "Enter", Key.Escape => "Escape", Key.Back => "Backspace", Key.Tab => "Tab", Key.Space => "Space",
            Key.OemMinus => "Minus", Key.OemPlus => "Equal", Key.OemOpenBrackets => "LeftBracket", Key.OemCloseBrackets => "RightBracket",
            Key.OemPipe => "Backslash", Key.OemSemicolon => "Semicolon", Key.OemQuotes => "Quote", Key.OemTilde => "Grave",
            Key.OemComma => "Comma", Key.OemPeriod => "Dot", Key.OemQuestion => "Slash", Key.Capital => "CapsLock",
            Key.PrintScreen => "PrintScreen", Key.Scroll => "ScrollLock", Key.Pause => "Pause", Key.Insert => "Insert",
            Key.Home => "Home", Key.PageUp => "PageUp", Key.Delete => "Delete", Key.End => "End", Key.PageDown => "PageDown",
            Key.Right => "Right", Key.Left => "Left", Key.Down => "Down", Key.Up => "Up", Key.NumLock => "NumLock",
            Key.Divide => "NumPadSlash", Key.Multiply => "NumPadAsterisk", Key.Subtract => "NumPadMinus",
            Key.Add => "NumPadPlus", Key.Decimal => "NumPadDot", Key.Apps => "Application",
            _ => null,
        };
    }
}
