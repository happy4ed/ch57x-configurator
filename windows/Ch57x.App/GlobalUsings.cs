// WPF + WinForms 를 함께 쓰면 Application/MessageBox 가 양쪽에 있어 모호해짐.
// 앱 전역에서 이 둘은 WPF 쪽으로 고정한다. (WinForms 타입은 각 파일에서 WinForms. 별칭 사용)
global using System.IO;
// WPF + WinForms 둘 다 쓰는 앱이라 충돌하는 타입은 WPF 쪽으로 alias 고정한다.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Button = System.Windows.Controls.Button;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Color = System.Windows.Media.Color;
global using Orientation = System.Windows.Controls.Orientation;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment = System.Windows.VerticalAlignment;
global using Binding = Ch57x.Core.Binding;
global using TextBox = System.Windows.Controls.TextBox;
global using ComboBox = System.Windows.Controls.ComboBox;
global using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
global using Label = System.Windows.Controls.Label;
global using Point = System.Windows.Point;
global using Size = System.Windows.Size;
// 0.3.0 에서 더 쓰는 WPF 타입 (WinForms 와 이름이 같음)
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using Cursors = System.Windows.Input.Cursors;
global using Clipboard = System.Windows.Clipboard;
global using RadioButton = System.Windows.Controls.RadioButton;
global using CheckBox = System.Windows.Controls.CheckBox;
global using ContextMenu = System.Windows.Controls.ContextMenu;
global using MenuItem = System.Windows.Controls.MenuItem;
global using Separator = System.Windows.Controls.Separator;
global using ProgressBar = System.Windows.Controls.ProgressBar;
global using Image = System.Windows.Controls.Image;
global using FontFamily = System.Windows.Media.FontFamily;
global using Rect = System.Windows.Rect;
global using Pen = System.Windows.Media.Pen;
