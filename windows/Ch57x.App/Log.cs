using System.Collections.ObjectModel;
using System.Windows;

namespace Ch57x.App;

/// <summary>앱 로그 — 「로그 보기」 창에 그대로 보인다(최근 500줄).</summary>
public static class Log
{
    public const int MaxLines = 500;
    public static ObservableCollection<string> Lines { get; } = new();

    public static void Write(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        void add()
        {
            Lines.Add(line);
            while (Lines.Count > MaxLines) Lines.RemoveAt(0);
        }
        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess()) app.Dispatcher.Invoke(add);
        else add();
    }

    /// <summary>예외는 한 줄로 남긴다(조용히 삼키지 않음).</summary>
    public static void Error(string context, Exception ex) => Write($"오류 — {context}: {ex.GetType().Name} — {ex.Message}");
}
