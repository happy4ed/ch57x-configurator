using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;   // Hyperlink
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace Ch57x.App;

/// <summary>
/// 정보 창 — 이름·버전·저작권·출처, 그리고 <b>이 PC 에서의 실제 위치</b>.
/// 값은 손으로 적지 않고 <see cref="Assembly"/> 메타데이터(csproj 한 곳)에서 읽는다.
/// 자동실행 상태와 실행 경로를 함께 보이는 이유: 문제가 생겼을 때 사람이 맨 먼저 확인할 자리다
/// (2026-09-13: 임시 폴더에서 실행 중인 줄 몰라 자동시작이 안 되는 것을 오래 못 찾았다).
/// </summary>
public partial class AboutWindow : Window
{
    private readonly string _profileFolder;

    /// <summary>
    /// 한 줄 설치 명령. README 의 「한 줄 설치(PowerShell)」 절, 저장소 루트 install.ps1 머리말과
    /// <b>같은 문자열</b>이어야 한다 — 고칠 때 세 곳을 함께 고친다.
    /// TLS1.2 를 명시하는 이유는 구형 Windows 10 의 PowerShell 5.1 이 기본값으로 GitHub 에 붙지 못해서고,
    /// Unblock-File 은 내려받은 스크립트의 출처 표식(Mark of the Web)을 지워 실행이 막히지 않게 한다.
    /// </summary>
    private const string InstallCommand =
        "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; " +
        "$p=\"$env:TEMP\\ch57x-install.ps1\"; " +
        "iwr -UseBasicParsing 'https://raw.githubusercontent.com/happy4ed/ch57x-configurator/master/install.ps1' -OutFile $p; " +
        "Unblock-File $p; " +
        "powershell -NoProfile -ExecutionPolicy Bypass -File $p";

    /// <param name="profileFolder">프로필 폴더 — 여는 자리가 하나여야 해서 호출부(TrayIcon)가 넘긴다.</param>
    public AboutWindow(string profileFolder)
    {
        _profileFolder = profileFolder;
        InitializeComponent();

        var asm = Assembly.GetExecutingAssembly();
        string Meta<T>(Func<T, string?> pick) where T : Attribute
            => asm.GetCustomAttribute<T>() is { } a ? pick(a) ?? "" : "";

        var product = Meta<AssemblyProductAttribute>(a => a.Product);
        var title = Meta<AssemblyTitleAttribute>(a => a.Title);
        var version = Meta<AssemblyInformationalVersionAttribute>(a => a.InformationalVersion);
        if (string.IsNullOrWhiteSpace(version)) version = asm.GetName().Version?.ToString() ?? "";
        // "0.2.0+abc1234" 처럼 빌드 메타데이터가 붙으면 앞부분만 보인다.
        var plus = version.IndexOf('+');
        var versionShort = plus > 0 ? version[..plus] : version;

        TitleText.Text = string.IsNullOrWhiteSpace(product) ? "CH57x 설정기" : product;
        VersionText.Text = string.IsNullOrWhiteSpace(versionShort) ? "" : $"버전 {versionShort}";
        DescText.Text = Meta<AssemblyDescriptionAttribute>(a => a.Description);
        if (string.IsNullOrWhiteSpace(DescText.Text)) DescText.Text = title;
        CopyrightText.Text = Meta<AssemblyCopyrightAttribute>(a => a.Copyright);

        try { AppIcon.Source = new BitmapImage(new Uri("pack://application:,,,/icon.ico")); } catch { }

        ExePathText.Text = "실행 파일: " + (Environment.ProcessPath ?? "(알 수 없음)");
        AutoStartText.Text = AutoStart.Describe();
        InstallCmdText.Text = InstallCommand;

        BtnClose.Click += (_, _) => Close();
        BtnProfiles.Click += (_, _) => OpenProfiles();
        BtnCopy.Click += (_, _) => CopyAll();
        BtnCopyInstall.Click += (_, _) => CopyInstall();
        AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OpenLink));
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

    /// <summary>문제를 알릴 때 그대로 붙여 넣을 수 있게 한 덩어리로 복사한다.</summary>
    private void CopyAll()
    {
        var text = string.Join(Environment.NewLine,
            $"{TitleText.Text} {VersionText.Text}",
            CopyrightText.Text,
            ExePathText.Text,
            AutoStartText.Text,
            $"OS: {Environment.OSVersion} / .NET {Environment.Version}");
        try { System.Windows.Clipboard.SetText(text); BtnCopy.Content = "복사됨"; }
        catch (Exception ex) { Log.Error("정보 복사", ex); }
    }

    /// <summary>
    /// 설치 명령을 클립보드로 보낸다. 사람이 이 창에서 드래그로 긁다가 일부만 복사해
    /// 엉뚱한 명령을 실행하는 일을 막으려고 버튼을 따로 둔다.
    /// </summary>
    private void CopyInstall()
    {
        try { System.Windows.Clipboard.SetText(InstallCommand); BtnCopyInstall.Content = "복사됨"; }
        catch (Exception ex) { Log.Error("설치 명령 복사", ex); }
    }
}
