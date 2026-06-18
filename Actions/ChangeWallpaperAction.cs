using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SystemTools.Settings;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ChangeWallpaper", "切换壁纸", "\uE9BC", false)]
public class ChangeWallpaperAction(ILogger<ChangeWallpaperAction> logger) : ActionBase<ChangeWallpaperSettings>
{
    private readonly ILogger<ChangeWallpaperAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ChangeWallpaperAction OnInvoke 开始");

        if (Settings == null || string.IsNullOrWhiteSpace(Settings.ImagePath))
        {
            _logger.LogWarning("图片路径为空");
            return;
        }

        if (!File.Exists(Settings.ImagePath))
        {
            _logger.LogError("图片文件不存在: {Path}", Settings.ImagePath);
            throw new FileNotFoundException("指定的图片文件不存在", Settings.ImagePath);
        }

        try
        {
            var imagePath = Settings.ImagePath;
            var fit = Settings.FitStyle;
            _logger.LogInformation("正在切换壁纸到: {Path}, FitStyle: {Fit}", imagePath, fit);

            // 根据 fitStyle 计算注册表值（TileWallpaper, WallpaperStyle）
            var (tileValue, styleValue) = fit switch
            {
                0 => ("1", "1"),    // 平铺：TileWallpaper=1, WallpaperStyle=1
                1 => ("0", "0"),    // 居中：TileWallpaper=0, WallpaperStyle=0
                2 => ("0", "2"),    // 拉伸：TileWallpaper=0, WallpaperStyle=2
                3 => ("0", "6"),    // 填充：TileWallpaper=0, WallpaperStyle=6
                4 => ("0", "10"),   // 适应：TileWallpaper=0, WallpaperStyle=10
                5 => ("0", "22"),   // 跨区：TileWallpaper=0, WallpaperStyle=22
                _ => ("0", "2")     // 默认：拉伸
            };

            // 在后台线程执行可能阻塞的注册表与系统 API 操作，避免阻塞宿主 UI
            await Task.Run(() =>
            {
                // 1. 修改注册表以设置契合度
                using (var desktopRegKey = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop", true))
                {
                    if (desktopRegKey == null)
                    {
                        throw new Win32Exception("无法访问系统桌面注册表配置项，操作失败。");
                    }

                    desktopRegKey.SetValue("TileWallpaper", tileValue, RegistryValueKind.String);
                    desktopRegKey.SetValue("WallpaperStyle", styleValue, RegistryValueKind.String);
                }

                // 2. 调用 SystemParametersInfo 设置壁纸并通知系统
                IntPtr uniPtr = IntPtr.Zero;
                try
                {
                    uniPtr = Marshal.StringToHGlobalUni(imagePath);
                    bool result;
                    unsafe
                    {
                        void* uniVoidPtr = (void*)uniPtr;
                        result = PInvoke.SystemParametersInfo(
                            Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETDESKWALLPAPER,
                            0,
                            uniVoidPtr,
                            Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_UPDATEINIFILE |
                            Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
                    }

                    if (!result)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "SystemParametersInfo失败");
                    }
                }
                finally
                {
                    if (uniPtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(uniPtr);
                }
            });

            _logger.LogInformation("切换壁纸成功: {Path}", imagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换壁纸失败");
            // 保持向上抛出异常，让上层 UI/宿主决定如何反馈给用户
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("ChangeWallpaperAction OnInvoke 完成");
    }
}
