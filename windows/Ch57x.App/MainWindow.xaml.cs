using System.Collections.Specialized;
using System.Windows;
using WinForms = System.Windows.Forms;

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

        BtnConnect.Click += (_, _) => _ctrl.Connect();
        BtnLoad.Click += (_, _) => LoadDialog();
        BtnUpload.Click += (_, _) => _ctrl.Upload();
        BtnRead.Click += (_, _) => _ctrl.ReadFromDevice();
        BtnSave.Click += (_, _) => SaveDialog();
        BtnClearLog.Click += (_, _) => Log.Lines.Clear();

        // hide instead of close (stay resident in tray)
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

    private void LoadDialog()
    {
        var dlg = new WinForms.OpenFileDialog { Filter = "프로필 JSON|*.json|모든 파일|*.*" };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK) _ctrl.LoadProfile(dlg.FileName);
    }

    private void SaveDialog()
    {
        var dlg = new WinForms.SaveFileDialog { Filter = "프로필 JSON|*.json", FileName = (_ctrl.Profile.Name ?? "profile") + ".json" };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK) _ctrl.SaveProfile(dlg.FileName);
    }
}
