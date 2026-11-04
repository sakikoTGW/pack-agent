namespace Pad.Core;

/// <summary>设置读取的唯一入口。模块不许直接摸全局单例。</summary>
public interface ISettingsService
{
    PadSettings Settings();
}
