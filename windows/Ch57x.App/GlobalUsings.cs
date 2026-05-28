// WPF + WinForms 를 함께 쓰면 Application/MessageBox 가 양쪽에 있어 모호해짐.
// 앱 전역에서 이 둘은 WPF 쪽으로 고정한다. (WinForms 타입은 각 파일에서 WinForms. 별칭 사용)
global using System.IO;
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
