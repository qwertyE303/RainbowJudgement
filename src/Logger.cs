using System;
using UnityModManagerNet;

namespace RainbowJudgement
{
    /// <summary>日志封装（统一输出到 UnityModManager 日志）</summary>
    public static class Logger
    {
        public static void Log(string msg)
        {
            try { UnityModManager.Logger.Log(msg); }
            catch { }
        }

        public static void Warn(string msg)
        {
            try { UnityModManager.Logger.Log("[WARN] " + msg); }
            catch { }
        }

        /// <summary>低频调用的守卫：把异常收敛成一行日志，避免 Mod 的补丁把游戏流程带崩。
        /// 注意：每次调用会分配一个委托，不要用在逐帧/逐判定的热路径上。</summary>
        public static void Guard(string tag, Action action)
        {
            try { action(); }
            catch (Exception ex) { Log("[" + tag + "] " + ex.Message); }
        }
    }
}
