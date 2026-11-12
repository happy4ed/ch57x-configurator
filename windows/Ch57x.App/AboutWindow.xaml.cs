using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace Ch57x.App;

/// <summary>
/// 정보 창 — 이름 · 판 · 저작권 · 출처 · 업데이트(접이 칸) · 문제 해결(이 PC 의 실제 위치).
/// 값은 손으로 적지 않고 어셈블리 정보(csproj 한 곳)에서 읽는다.
/// </summary>
public partial class AboutWindow : FluentWindow
{
    private readonly string _profileFolder;

    /// <summary>
    /// 한 줄 설치 명령. README 「한 줄 설치(PowerShell)」 · 저장소 루트 install.ps1 머리말과 ★같은 문자열 — 고칠 때 세 곳을 함께.
    /// </summary>
    public const string InstallCommand =
        "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; " +
        "$p=\"$env:TEMP\\ch57x-install.ps1\"; " +
        "iwr -UseBasicParsing 'https://raw.githubusercontent.com/happy4ed/ch57x-configurator/master/install.ps1' -OutFile $p; " +
        "Unblock-File $p; " +
        "powershell -NoProfile -ExecutionPolicy Bypass -File $p";

    /// <param name="exePathOverride">렌더 모드 견본 경로(실제 경로 · 레지스트리를 안 읽음)</param>
    public AboutWindow(string profileFolder, bool openUpdate = false, string? exePathOverride = null, string? autoStartOverride = null)
    {
        _profileFolder = profileFolder;
        InitializeComponent();

        var asm = Assembly.GetExecutingAssembly();
        string Meta<T>(Func<T, string?> pick) where T : Attribute => asm.GetCustomAttribute<T>() is { } a ? pick(a) ?? "" : "";
        var product = Meta<AssemblyProductAttribute>(a => a.Product);
        var copyright = Meta<AssemblyCopyrightAttribute>(a => a.Copyright);
        TitleText.Text = string.IsNullOrWhiteSpace(product) ? "CH57x 설정기" : product;
        // 「© 2026 happy4ed. MIT License.」 → 「© 2026 happy4ed · MIT 라이선스」
        var holder = copyright.Replace(". MIT License.", "").Replace("MIT License.", "").Trim().TrimEnd('.');
        VersionText.Text = $"버전 {ErrorReport.Version()} · {holder} · MIT 라이선스";
        DescText.Text = "CH57x 칩 미니 매크로 키보드(VID 1189) 설정 도구";

        ExePathText.Text = exePathOverride ?? Environment.ProcessPath ?? "(알 수 없음)";
        AutoStartText.Text = autoStartOverride ?? AutoStartText_();
        InstallCmdText.Text = InstallCommand;
        UpdateBox.IsExpanded = openUpdate;

        BtnClose.Click += (_, _) => Close();
        BtnProfiles.Click += (_, _) => OpenProfiles();
        BtnCopy.Click += (_, _) => { if (Dialogs.Clip(InfoText())) BtnCopy.Content = "복사됨"; };
        BtnCopyInstall.Click += (_, _) => { if (Dialogs.Clip(InstallCommand)) BtnCopyInstall.Content = "복사됨"; };
        BtnShowCmd.Click += (_, _) =>
        {
            bool show = InstallCmdText.Visibility != Visibility.Visible;
            InstallCmdText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            BtnShowCmd.Content = show ? "명령 숨기기" : "명령 보기";
        };
        AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OpenLink));
    }

    private static string AutoStartText_()
    {
        try { return AutoStart.Describe().Replace("자동실행: ", ""); }
        catch (Exception ex) { return $"확인 못 함 — {ex.Message}"; }
    }

    private static void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("링크 열기", ex); }
        e.Handled = true;
    }

    private void OpenProfiles()
    {
        try
        {
            Directory.CreateDirectory(_profileFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", _profileFolder) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Error("프로필 폴더 열기", ex); }
    }

    /// <summary>문제를 알릴 때 그대로 붙여 넣을 한 덩어리</summary>
    private string InfoText() => string.Join(Environment.NewLine,
        ErrorReport.Environment_(),
        $"실행 파일: {ExePathText.Text}",
        $"자동실행: {AutoStartText.Text}",
        $"프로필 폴더: {_profileFolder}");
}
