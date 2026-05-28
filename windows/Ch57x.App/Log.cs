using System.Collections.ObjectModel;
using System.Windows;

namespace Ch57x.App;

/// <summary>App-wide log shown live in the main window — the primary debugging aid.</summary>
public static class Log
{
    public static ObservableCollection<string> Lines { get; } = new();

    public static void Write(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        void add()
        {
            Lines.Add(line);
            while (Lines.Count > 500) Lines.RemoveAt(0);
        }
        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess()) app.Dispatcher.Invoke(add);
        else add();
    }

    /// <summary>Log an exception with full detail (no silent failures — easy debugging).</summary>
    public static void Error(string context, Exception ex) =>
        Write($"❌ {context}: {ex.GetType().Name} — {ex.Message}");
}
