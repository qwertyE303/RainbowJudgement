using System;

namespace RainbowJudgement
{
    /// <summary>日志封装（输出到 UnityModManager 日志）</summary>
    public static class Logger
    {
        public static void Log(string msg)
        {
            try { UnityModManagerNet.UnityModManager.Logger.Log(msg); }
            catch { }
        }
        public static void Warn(string msg)
        {
            try { UnityModManagerNet.UnityModManager.Logger.Log("[WARN] " + msg); }
            catch { }
        }
    }
}


