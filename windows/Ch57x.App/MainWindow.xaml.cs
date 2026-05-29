using System.Collections.Specialized;
using System.Windows;

namespace Ch57x.App;

public partial class MainWindow : Window
{
    private readonly Controller _ctrl;

    public MainWindow(Controller ctrl)
    {
        InitializeComponent();
        _ctrl = ctrl;

        LogList.ItemsSource = Log.Lines;
        Log.Lines.CollectionChanged += OnLogChanged;

        _ctrl.Changed += RefreshStatus;
        RefreshStatus();

        BtnUpload.Click += (_, _) => _ctrl.Upload();
        BtnRead.Click += (_, _) => _ctrl.ReadFromDevice();
        BtnClearLog.Click += (_, _) => Log.Lines.Clear();

        // 닫지 않고 숨김 (트레이 상주)
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    private void OnLogChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private void RefreshStatus()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(RefreshStatus); return; }
        StatusText.Text = _ctrl.IsConnected ? "● 연결됨" : "○ 미연결";
        StatusText.Foreground = _ctrl.IsConnected
            ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Gray;
        var p = _ctrl.Profile;
        ProfileText.Text = $"프로필: {p.Name} · 키 {p.KeyCount}/노브 {p.KnobCount}";
    }
}
