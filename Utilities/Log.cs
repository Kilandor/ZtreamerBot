using BepInEx.Logging;

namespace ZtreamerBot;

public partial class Utilities
{
    public enum LogLevel
    {
        Info = 0,
        Warning = 1,
        Debug = 2,
        Error = 3
    }
    
    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        ManualLogSource Logger = Plugin.baseLogger;
        switch (level)
        {
            default:
            case LogLevel.Info:
                Logger.LogInfo(message);
                break;
            case LogLevel.Warning:
                Logger.LogWarning(message);
                break;
            case LogLevel.Debug:
                if (Plugin.Instance.debugEnabled.Value)
                    Logger.LogDebug(message);
                break;
            case LogLevel.Error:
                Logger.LogError(message);
                break;
        }
    }
}