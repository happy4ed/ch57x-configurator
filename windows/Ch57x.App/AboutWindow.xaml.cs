using System.Diagnostics;
using System.Reflection;
using System.Windows;
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

        BtnClose.Click += (_, _) => Close();
        BtnProfiles.Click += (_, _) => OpenProfiles();
        BtnCopy.Click += (_, _) => CopyAll();
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
        try { Clipboard.SetText(text); BtnCopy.Content = "복사됨"; }
        catch (Exception ex) { Log.Error("정보 복사", ex); }
    }
}
