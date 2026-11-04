namespace Pad.Core;

/// <summary>
/// 平台入口：按运行时 OS 选实现。核心代码只认 <see cref="IPlatform"/>，
/// 不出现 Win32 / cmd.exe / junction。
/// </summary>
public static class Platform
{
    public static IPlatform Current { get; } = OperatingSystem.IsWindows()
        ? new WindowsPlatform()
        : new MacPlatform();
}
