using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Management;
using System.Threading.Tasks;

namespace SystemTools.Actions;

/// <summary>
/// 调整屏幕亮度
/// </summary>
[ActionInfo("SystemTools.AdjustScreenBrightness", "调整屏幕亮度", "\uE706", false)]
public class AdjustScreenBrightnessAction(ILogger<AdjustScreenBrightnessAction> logger) 
    : ActionBase<AdjustScreenBrightnessSettings>
{
    private readonly ILogger<AdjustScreenBrightnessAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        try
        {
            if (Settings == null)
            {
                _logger.LogWarning("设置为空，无法调整屏幕亮度");
                return;
            }

            // 验证亮度值范围
            if (Settings.BrightnessPercent < 1 || Settings.BrightnessPercent > 100)
            {
                _logger.LogWarning("亮度值 {Brightness} 超出范围 1-100", Settings.BrightnessPercent);
                throw new ArgumentOutOfRangeException(nameof(Settings.BrightnessPercent), 
                    "亮度值必须在 1-100 之间");
            }

            bool success = SetScreenBrightnessWmi(Settings.BrightnessPercent);
            
            if (success)
            {
                _logger.LogInformation("屏幕亮度已设置为 {Brightness}%", Settings.BrightnessPercent);
            }
            else
            {
                _logger.LogWarning("设置屏幕亮度失败，可能是硬件不支持或 WMI 调用失败");
                throw new Exception("设置屏幕亮度失败，请检查硬件是否支持亮度调整");
            }

            await base.OnInvoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调整屏幕亮度时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 使用 WMI 设置屏幕亮度
    /// </summary>
    /// <param name="brightness">亮度值 1-100</param>
    /// <returns>是否设置成功</returns>
    private bool SetScreenBrightnessWmi(int brightness)
    {
        try
        {
            // 创建 WMI 连接
            var scope = new ManagementScope("\\\\.\\root\\cimv2");
            scope.Connect();

            // 查询 WmiMonitorBrightnessMethods
            var query = new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods");
            var searcher = new ManagementObjectSearcher(scope, query);
            var objects = searcher.Get();

            if (objects.Count == 0)
            {
                _logger.LogWarning("未找到支持亮度调整的显示器设备");
                return false;
            }

            foreach (ManagementObject obj in objects)
            {
                try
                {
                    // 调用 WmiMonitorBrightnessMethods 的 WmiSetBrightness 方法
                    var inParams = obj.GetMethodParameters("WmiSetBrightness");
                    inParams["Brightness"] = brightness;
                    inParams["Timeout"] = 0;

                    var outParams = obj.InvokeMethod("WmiSetBrightness", inParams, null);

                    if (outParams != null)
                    {
                        _logger.LogInformation("WmiSetBrightness 调用返回状态: {Status}", outParams["returnValue"]);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "调用 WmiSetBrightness 失败");
                    continue;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WMI 操作异常");
            return false;
        }
    }
}
