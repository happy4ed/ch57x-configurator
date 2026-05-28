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
