using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>
/// 키 설정 대화상자 — 2판 모형(dlg-key · dlg-text) 그대로.
/// D4 키 칸 하나 + 「눌러서 입력」 · D5 동작 = 라디오 · 수정자 = 체크 상자 · D6 단계가 하나면 상자 없음 · 「단계 1 / 18」 ·
/// D7 제목 = 대상(「키 1 · 레이어 3」 — Terms.KeyTitle) · 라벨 「이름」 · 단위 ms 는 칸 뒤 · D2 「이 키 비우기」 따로 한 줄 · S11 Enter 적용 · Esc 취소 ·
/// S12 상용구 「13 / 18」 · 못 넣는 글자 즉시 안내 · 입력 한도 18 · 못 넣는 글자가 있으면 「적용」 잠김(첫 사용 점검 4번).
/// 첫 사용 점검 3번: 키 동작의 설명 줄은 「눌러서 입력」 한 줄만(단계 수 · 브라우저 안내는 뺌 — 「단계 1 / 18」과 겹치고 PC 앱엔 브라우저가 없음).
/// </summary>
public partial class KeyDialog : FluentWindow
{
    private static readonly (string V, string Label)[] Types =
    {
        ("key", "키보드"), ("text", "상용구"), ("media", "미디어"), ("mouse", "마우스"), ("none", "없음"),
    };

    private string _type;
    public Binding? Result { get; private set; }

    private List<Accord> _steps = new() { new Accord() };
    private int _delay;
    private string _text = "";
    private string _media = "VolumeUp";
    private string _mouseAct = "click";
    private List<string> _mouseMods = new();
    private List<string> _mouseBtns = new();
    private int _dx, _dy, _delta = 1;
    private int? _capture;           // 「눌러서 입력」 중인 단계

    public KeyDialog(string title, Binding? current)
    {
        InitializeComponent();
        Title = title;
        var init = current ?? new Binding { Type = BindingType.None };
        TxtAlias.Text = init.Alias ?? "";
        _type = init.Type switch { BindingType.Key => "key", BindingType.Text => "text", BindingType.Media => "media", BindingType.Mouse => "mouse", _ => "none" };
        if (init.Type == BindingType.Key && init.Steps?.Count > 0)
        {
            _steps = init.Steps.Select(s => new Accord { Mods = s.Mods?.ToList() ?? new(), Code = s.Code }).ToList();
            _delay = init.Delay;
        }
        else if (init.Type == BindingType.Text) { _text = init.Text ?? ""; _delay = init.Delay; }
        else if (init.Type == BindingType.Media) _media = init.Media ?? "VolumeUp";
        else if (init.Type == BindingType.Mouse)
        {
            _mouseAct = init.Action ?? "click";
            _mouseMods = init.Mods?.ToList() ?? new();
            _mouseBtns = init.Buttons?.ToList() ?? new();
            _dx = init.Dx; _dy = init.Dy; _delta = init.Delta == 0 ? 1 : init.Delta;
        }
        if (_type == "none") _type = "key";   // 빈 키를 열면 키보드부터 (「없음」은 라디오로)
        if (current == null || current.Type == BindingType.None) _steps = new() { new Accord() };

        foreach (var (v, label) in Types)
        {
            var r = new RadioButton { Content = label, GroupName = "type", IsChecked = v == _type, Margin = new Thickness(0, 0, 16, 0) };
            string vv = v;
            r.Checked += (_, _) => { _type = vv; _capture = null; RenderBody(); };
            TypeRadios.Children.Add(r);
        }
        RenderBody();

        BtnApply.Click += (_, _) => { Result = BuildResult(); DialogResult = true; };
        ToolTipService.SetShowOnDisabled(BtnApply, true);
        BtnClear.Click += (_, _) => { Result = new Binding { Type = BindingType.None }; DialogResult = true; };
        PreviewKeyDown += OnCaptureKey;
        Loaded += (_, _) => TxtAlias.Focus();
    }

    // ───────────────────────── 화면

    private static TextBlock Header(string s, double top = 0)
    {
        var t = new TextBlock { Text = s, Margin = new Thickness(0, top, 0, 8) };
        t.SetResourceReference(StyleProperty, "Strong");
        t.VerticalAlignment = VerticalAlignment.Top;
        return t;
    }

    private static TextBlock Caption(string s, double top = 8)
    {
        var t = new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
        t.SetResourceReference(StyleProperty, "Caption");
        return t;
    }

    private void RenderBody()
    {
        Body.Children.Clear();
        BtnApply.IsEnabled = true;   // 상용구만 못 넣는 글자가 있을 때 잠근다(RenderText)
        switch (_type)
        {
            case "key": RenderKey(); break;
            case "text": RenderText(); break;
            case "media": RenderMedia(); break;
            case "mouse": RenderMouse(); break;
            default: Body.Children.Add(Caption("이 키는 비어 있음 — 눌러도 아무 일 없음", 0)); break;
        }
    }

    private void RenderKey()
    {
        if (_steps.Count == 0) _steps.Add(new Accord());
        bool many = _steps.Count > 1;
        for (int i = 0; i < _steps.Count; i++)
        {
            var content = StepContent(i);
            if (!many) { Body.Children.Add(content); continue; }
            // 단계가 여럿일 때만 상자 (D6)
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            int idx = i;
            var del = new Button { Tag = "trash", ToolTip = $"단계 {i + 1} 빼기" };
            del.SetResourceReference(StyleProperty, "IconButton");
            System.Windows.Automation.AutomationProperties.SetName(del, $"단계 {i + 1} 빼기");
            del.Click += (_, _) => { _steps.RemoveAt(idx); _capture = null; RenderBody(); };
            DockPanel.SetDock(del, Dock.Right);
            head.Children.Add(del);
            var ht = new TextBlock { Text = $"단계 {i + 1}" };
            ht.SetResourceReference(StyleProperty, "Strong");
            head.Children.Add(ht);
            var card = new Border { Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Child = new StackPanel { Children = { head, content } } };
            card.SetResourceReference(StyleProperty, "CardBox");
            Body.Children.Add(card);
        }
        Body.Children.Add(Caption("「눌러서 입력」 뒤 키를 누르면 왼쪽 칸에 들어감"));

        var stepRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var count = new TextBlock { Text = $"단계 {_steps.Count} / {TextMacro.MaxKeys}" };
        count.SetResourceReference(StyleProperty, "Caption");
        DockPanel.SetDock(count, Dock.Right);
        stepRow.Children.Add(count);
        if (_steps.Count < TextMacro.MaxKeys)
        {
            var add = new Button { Content = "단계 추가", Tag = "plus", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 0, 11, 0) };
            add.SetResourceReference(StyleProperty, "SubtleButton");
            add.Click += (_, _) => { _steps.Add(new Accord()); RenderBody(); };
            stepRow.Children.Add(add);
        }
        Body.Children.Add(stepRow);

        // 지연 — 단위 ms 는 칸 뒤 (D7)
        var delayRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var dl = new TextBlock { Text = "지연", Width = 48 };
        dl.SetResourceReference(StyleProperty, "Strong");
        delayRow.Children.Add(dl);
        var dly = new TextBox { Width = 96, Text = _delay.ToString() };
        System.Windows.Automation.AutomationProperties.SetName(dly, "지연 (ms)");
        dly.TextChanged += (_, _) => { if (int.TryParse(dly.Text, out var v)) _delay = Math.Clamp(v, 0, 6000); else if (dly.Text.Length == 0) _delay = 0; };
        delayRow.Children.Add(dly);
        var ms = new TextBlock { Text = "ms", Margin = new Thickness(8, 0, 0, 0) };
        ms.SetResourceReference(StyleProperty, "Caption");
        delayRow.Children.Add(ms);
        Body.Children.Add(delayRow);
    }

    private FrameworkElement StepContent(int i)
    {
        var s = _steps[i];
        var sp = new StackPanel();
        sp.Children.Add(Header("수정자"));
        var mods = new WrapPanel();
        foreach (var m in new[] { "Ctrl", "Shift", "Alt", "Win" })
        {
            string mm = m;
            var cb = new CheckBox { Content = m, IsChecked = s.Mods?.Contains(m) == true, Margin = new Thickness(0, 0, 16, 0) };
            cb.Checked += (_, _) => { s.Mods ??= new(); if (!s.Mods.Contains(mm)) s.Mods.Add(mm); };
            cb.Unchecked += (_, _) => s.Mods?.Remove(mm);
            mods.Children.Add(cb);
        }
        sp.Children.Add(mods);

        sp.Children.Add(Header("키", 16));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var combo = new ComboBox { Width = 224 };
        System.Windows.Automation.AutomationProperties.SetName(combo, "키");
        combo.Items.Add(new ComboBoxItem { Content = "(없음)", Tag = "" });
        foreach (var (name, label) in KeyCodes.Order)
        {
            var it = new ComboBoxItem { Content = label == name ? label : $"{label} ({name})", Tag = name };
            combo.Items.Add(it);
            if (name == s.Code) combo.SelectedItem = it;
        }
        if (combo.SelectedItem == null) combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) => s.Code = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
        row.Children.Add(combo);
        bool capturing = _capture == i;
        var cap = new Button { Content = capturing ? "키를 누르세요…" : "눌러서 입력", Tag = "keyboard", Margin = new Thickness(8, 0, 0, 0) };
        if (capturing) cap.SetResourceReference(StyleProperty, "AccentButton");
        int idx = i;
        cap.Click += (_, _) => { _capture = _capture == idx ? null : idx; RenderBody(); };
        row.Children.Add(cap);
        sp.Children.Add(row);
        return sp;
    }

    private void RenderText()
    {
        Body.Children.Add(Header("상용구"));
        var tb = new TextBox
        {
            Text = _text, MaxLength = TextMacro.MaxKeys, TextWrapping = TextWrapping.Wrap, Height = 64,
            VerticalContentAlignment = VerticalAlignment.Top, Tag = "영문 · 숫자 · 기호",
        };
        System.Windows.Automation.AutomationProperties.SetName(tb, "상용구");
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var counter = new TextBlock();
        counter.SetResourceReference(StyleProperty, "Caption");
        DockPanel.SetDock(counter, Dock.Right);
        var warn = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        warn.SetResourceReference(TextBlock.ForegroundProperty, "Critical");
        row.Children.Add(counter);
        row.Children.Add(warn);
        void update()
        {
            var a = TextMacro.Analyze(tb.Text);
            counter.Text = TextMacro.Counter(a);
            warn.Text = TextMacro.Warning(a) ?? "";
            // 못 넣는 글자가 있으면 적용 막기 — Enter(IsDefault)도 같이 막힌다 (첫 사용 점검 4번 · S12)
            BtnApply.IsEnabled = a.Unsupported.Count == 0 && !a.OverLimit;
            BtnApply.ToolTip = BtnApply.IsEnabled ? null : "넣을 수 없는 글자를 지우면 적용할 수 있음";
        }
        tb.TextChanged += (_, _) => { _text = tb.Text; update(); };
        update();
        Body.Children.Add(tb);
        Body.Children.Add(row);
        Body.Children.Add(Caption("글자 하나가 키 한 번.", 12));
    }

    private void RenderMedia()
    {
        Body.Children.Add(Header("미디어 키"));
        var combo = new ComboBox { Width = 224, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (name, (_, label)) in KeyCodes.MediaCodes)
        {
            var it = new ComboBoxItem { Content = label, Tag = name };
            combo.Items.Add(it);
            if (name == _media) combo.SelectedItem = it;
        }
        if (combo.SelectedItem == null) combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) => _media = (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "VolumeUp";
        Body.Children.Add(combo);
    }

    private void RenderMouse()
    {
        Body.Children.Add(Header("마우스 동작"));
        var act = new ComboBox { Width = 224, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (v, label) in new[] { ("click", "클릭"), ("wheel", "휠"), ("move", "이동"), ("drag", "끌기") })
        {
            var it = new ComboBoxItem { Content = label, Tag = v };
            act.Items.Add(it);
            if (v == _mouseAct) act.SelectedItem = it;
        }
        act.SelectionChanged += (_, _) => { _mouseAct = (act.SelectedItem as ComboBoxItem)?.Tag as string ?? "click"; RenderBody(); };
        Body.Children.Add(act);

        Body.Children.Add(Header("수정자", 16));
        Body.Children.Add(Checks(new[] { ("Ctrl", "Ctrl"), ("Shift", "Shift"), ("Alt", "Alt") }, _mouseMods));
        if (_mouseAct is "click" or "drag")
        {
            Body.Children.Add(Header("단추", 16));
            Body.Children.Add(Checks(new[] { ("Left", "왼쪽"), ("Right", "오른쪽"), ("Middle", "가운데") }, _mouseBtns));
        }
        if (_mouseAct == "wheel")
        {
            Body.Children.Add(Header("방향", 16));
            var wp = new WrapPanel();
            var up = new RadioButton { Content = "위", GroupName = "wheel", IsChecked = _delta >= 0, Margin = new Thickness(0, 0, 16, 0) };
            var dn = new RadioButton { Content = "아래", GroupName = "wheel", IsChecked = _delta < 0 };
            up.Checked += (_, _) => _delta = 1;
            dn.Checked += (_, _) => _delta = -1;
            wp.Children.Add(up); wp.Children.Add(dn);
            Body.Children.Add(wp);
        }
        if (_mouseAct is "move" or "drag")
        {
            Body.Children.Add(Header("이동", 16));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(NumField("가로", _dx, v => _dx = v));
            row.Children.Add(NumField("세로", _dy, v => _dy = v));
            Body.Children.Add(row);
        }
    }

    private static WrapPanel Checks((string V, string Label)[] items, List<string> into)
    {
        var wp = new WrapPanel();
        foreach (var (v, label) in items)
        {
            string vv = v;
            var cb = new CheckBox { Content = label, IsChecked = into.Contains(v), Margin = new Thickness(0, 0, 16, 0) };
            cb.Checked += (_, _) => { if (!into.Contains(vv)) into.Add(vv); };
            cb.Unchecked += (_, _) => into.Remove(vv);
            wp.Children.Add(cb);
        }
        return wp;
    }

    private static FrameworkElement NumField(string label, int initial, Action<int> on)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
        var t = new TextBlock { Text = label, Margin = new Thickness(0, 0, 8, 0) };
        t.SetResourceReference(StyleProperty, "Caption");
        sp.Children.Add(t);
        var box = new TextBox { Width = 72, Text = initial.ToString() };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        box.TextChanged += (_, _) => { if (int.TryParse(box.Text, out var v)) on(Math.Clamp(v, -127, 127)); };
        sp.Children.Add(box);
        return sp;
    }

    // ───────────────────────── 「눌러서 입력」

    private void OnCaptureKey(object sender, KeyEventArgs e)
    {
        if (_capture is not int idx || idx >= _steps.Count) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        e.Handled = true;   // 붙잡는 동안 Enter · Esc 도 키로 받는다(적용 · 취소로 새지 않게)
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        var name = WpfKeyToName(key);
        if (name == null) return;
        var s = _steps[idx];
        s.Code = name;
        s.Mods = new();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) s.Mods.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) s.Mods.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) s.Mods.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) s.Mods.Add("Win");
        _capture = null;
        RenderBody();
    }

    private static string? WpfKeyToName(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return ((char)('A' + (k - Key.A))).ToString();
        if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return "NumPad" + (k - Key.NumPad0);
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

    // ───────────────────────── 결과

    private Binding BuildResult()
    {
        string alias = TxtAlias.Text.Trim();
        Binding b = _type switch
        {
            "key" => BuildKeyBinding(),
            "text" => string.IsNullOrEmpty(_text) ? new Binding { Type = BindingType.None } : new Binding { Type = BindingType.Text, Text = _text, Delay = _delay },
            "media" => new Binding { Type = BindingType.Media, Media = _media },
            "mouse" => new Binding
            {
                Type = BindingType.Mouse, Action = _mouseAct, Mods = _mouseMods.ToList(), Buttons = _mouseBtns.ToList(),
                Dx = _dx, Dy = _dy, Delta = _mouseAct == "wheel" ? _delta : 0,
            },
            _ => new Binding { Type = BindingType.None },
        };
        if (b.Type != BindingType.None && alias.Length > 0) b.Alias = alias;
        return b;
    }

    private Binding BuildKeyBinding()
    {
        var steps = _steps.Where(s => (s.Mods?.Count ?? 0) > 0 || !string.IsNullOrEmpty(s.Code))
                          .Select(s => new Accord { Mods = s.Mods?.ToList() ?? new(), Code = string.IsNullOrEmpty(s.Code) ? null : s.Code }).ToList();
        return steps.Count == 0 ? new Binding { Type = BindingType.None } : new Binding { Type = BindingType.Key, Steps = steps, Delay = _delay };
    }
}
