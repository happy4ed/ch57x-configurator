using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ch57x.App;

/// <summary>앱 아이콘(신원 표식) — icon.ico 의 크기별 그림(16 · 20 · 24 · 32 · 48 · 256)에서 필요한 크기를 고른다.</summary>
public static class AppIcons
{
    private static readonly Uri IcoUri = new("pack://application:,,,/icon.ico", UriKind.Absolute);
    private static BitmapDecoder? _decoder;

    private static BitmapDecoder Decoder => _decoder ??= BitmapDecoder.Create(IcoUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

    /// <summary>픽셀 크기 px 이상 중 가장 작은 그림 (없으면 가장 큰 것)</summary>
    public static BitmapSource Frame(int px)
    {
        var frames = Decoder.Frames.OrderBy(f => f.PixelWidth).ToList();
        return frames.FirstOrDefault(f => f.PixelWidth >= px) ?? frames.Last();
    }

    public static ImageSource WindowIcon => Frame(32);

    /// <summary>제목 표시줄 16 DIP · 정보 창 48 DIP — 배율 150% 까지 선명하게 큰 그림을 줄여 쓴다.</summary>
    public static void Register(System.Windows.ResourceDictionary res)
    {
        res["AppIcon16"] = Frame(24);
        res["AppIcon48"] = Frame(256);
    }
}
