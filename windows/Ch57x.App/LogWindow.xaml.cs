using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using Ch57x.Core;

namespace Ch57x.App;

/// <summary>로그 보기 (판정 M1 · M2 · M3 · M5 · M6)</summary>
public partial class LogWindow : FluentWindow
{
    private readonly Controller _ctrl;
    public bool AllowClose { get; set; }

    public LogWindow(Controller ctrl)
    {
        InitializeComponent();
        _ctrl = ctrl;
        Title = Terms.WindowTitle(Terms.LogView);
        Lines.ItemsSource = Log.Lines;
        Log.Lines.CollectionChanged += OnLines;
        _ctrl.Changed += Refresh;
        BtnCopy.Click += (_, _) => { if (Dialogs.Clip(string.Join(Environment.NewLine, Log.Lines))) BtnCopy.Content = "복사됨"; };
        BtnClear.Click += (_, _) => Log.Lines.Clear();
        TrayHint.Visibility = _ctrl.Settings.TrayHintDismissed ? Visibility.Collapsed : Visibility.Visible;
        TrayHintOk.Click += (_, _) =>
        {
            TrayHint.Visibility = Visibility.Collapsed;
            _ctrl.Settings.TrayHintDismissed = true;
            _ctrl.Settings.Save();
        };
        Closing += (_, e) => { if (!AllowClose && !AppMode.Render) { e.Cancel = true; Hide(); } };
        Refresh();
    }

    private void OnLines(object? s, NotifyCollectionChangedEventArgs e) => Scroller.ScrollToEnd();

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var p = _ctrl.Profile;
        if (_ctrl.IsConnected)
        {
            StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Success");
            StatusText.Text = "연결됨";
            StatusCaption.Text = $"{_ctrl.DeviceName} · {p.KeyCount}키 · {p.KnobCount}노브 · 프로필 {_ctrl.ProfileName}";
        }
        else
        {
            StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "T3");
            StatusText.Text = "연결 안 됨";
            StatusCaption.Text = "키보드를 꽂으면 자동으로 연결됨";
        }
    }
}
