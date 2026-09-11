// WPF ImplicitUsings 引入 System.Windows.Shapes.Path，与 System.IO.Path 冲突。
// 通过全局 using 别名在测试项目内统一解析为 System.IO.Path。
global using Path = System.IO.Path;
global using Directory = System.IO.Directory;
global using File = System.IO.File;
global using SearchOption = System.IO.SearchOption;
global using Xunit;
global using Xunit.Abstractions;
