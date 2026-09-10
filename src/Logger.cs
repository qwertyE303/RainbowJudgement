using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityModManagerNet;

namespace RainbowJudgement
{
    /// <summary>
    /// 日志出口（两个目的地，职责分开）：
    ///   · DebugLog 门控的调试输出（每次判定的 [GetMargin]/[TickColor]、进度事件等）
    ///     → 只写 &lt;RJ Mod 目录&gt;\Log.txt（每次启动游戏清空重记），不再污染 UMM 的 Log.txt
    ///   · Warn（不变量不一致 / 占位补齐 / 异常）
    ///     → UMM 的 Log.txt **和** 上面那个文件都写一份（UMM 界面里能立刻看到问题，排查时文件里有上下文）
    ///
    /// Mod 目录定位：沿用 ModPaths 的「实际加载的 DLL 所在目录」策略（XPerfect 同款），
    /// 但**不缓存、每次重新取**——玩家把 mod 放 Mods / UMMMods / 任意目录名都能命中；
    /// 取不到就回退 UMM 报告的路径，再回退当前工作目录。
    /// 任何写盘失败都静默降级（只写 UMM），绝不因为日志把游戏带崩。
    /// </summary>
    public static class Logger
    {
        private static string _file;
        private static string _dir;
        private static bool _dead;      // 文件日志不可用 → 全部降级到 UMM
        private static bool _opened;

        public static string FilePath { get { return _file == null ? "" : _file; } }
        public static string Dir { get { return _dir == null ? "" : _dir; } }

        // ---------------- 初始化 ----------------

        /// <summary>在 Main.Load 里调用：定位 Mod 目录并清空 Log.txt（=「每次启动游戏清空重记」）</summary>
        public static void Init()
        {
            try
            {
                _dir = ResolveModDir();
                if (string.IsNullOrEmpty(_dir))
                {
                    _dead = true;
                    Warn("[Log] 无法定位 Mod 目录，日志回退到 UMM Log.txt");
                    return;
                }
                _file = Path.Combine(_dir, "Log.txt");
                using (StreamWriter w = new StreamWriter(_file, false, new UTF8Encoding(true)))
                {
                    w.Write("[RainbowJudgement] 日志开始（本文件每次启动游戏都会清空重记）\r\n");
                }
                _opened = true;
                Banner("Mod 目录=" + _dir + "（本文件每次启动游戏都会清空重记）");
            }
            catch (Exception ex)
            {
                _dead = true;
                Warn("[Log] 初始化失败（回退 UMM 日志）: " + ex.Message);
            }
        }

        /// <summary>Mod 目录：DLL 实际所在目录 → UMM 报告路径 → 当前目录（只保留真实存在的）</summary>
        private static string ResolveModDir()
        {
            List<string> cands = new List<string>();
            AddDir(cands, AssemblyDirOf(typeof(Logger).Assembly));
            AddDir(cands, AssemblyDirOf(Assembly.GetExecutingAssembly()));
            try { AddDir(cands, Main.ModPath); }
            catch { }
            AddDir(cands, Directory.GetCurrentDirectory());
            return cands.Count > 0 ? cands[0] : null;
        }

        private static string AssemblyDirOf(Assembly asm)
        {
            try
            {
                if (asm == null) return null;
                string loc = asm.Location;
                return string.IsNullOrEmpty(loc) ? null : Path.GetDirectoryName(loc);
            }
            catch { return null; }
        }

        private static void AddDir(List<string> list, string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return;
                string full = Path.GetFullPath(dir);
                if (!Directory.Exists(full)) return;
                foreach (string s in list)
                    if (string.Equals(s, full, StringComparison.OrdinalIgnoreCase)) return;
                list.Add(full);
            }
            catch { }
        }

        // ---------------- 写 ----------------

        /// <summary>调试日志（只进 Mod 目录的 Log.txt；由 DebugLog 开关门控——调用点不必再判断）</summary>
        public static void Log(string msg)
        {
            try
            {
                if (Main.Settings != null && !Main.Settings.DebugLog) return;
            }
            catch { return; }
            Write("[D] " + msg);
        }

        /// <summary>警告：UMM 与 Mod 目录的 Log.txt 都写（不受 DebugLog 开关影响）</summary>
        public static void Warn(string msg)
        {
            try { UnityModManager.Logger.Log("[WARN] " + msg); }
            catch { }
            Write("[W] " + msg);
        }

        /// <summary>启动横幅：不受 DebugLog 门控，保证日志文件一定存在且有加载时间戳</summary>
        public static void Banner(string msg)
        {
            Write("[I] " + msg);
        }

        private static void Write(string line)
        {
            if (_dead || !_opened) return;
            try
            {
                File.AppendAllText(_file, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "]" + line + "\r\n");
            }
            catch { _dead = true; }
        }

        /// <summary>低频调用的守卫：把异常收敛成一行日志，避免 Mod 的补丁把游戏流程带崩。
        /// 注意：每次调用会分配一个委托，不要用在逐帧/逐判定的热路径上。</summary>
        public static void Guard(string tag, Action action)
        {
            try { action(); }
            catch (Exception ex) { Warn("[" + tag + "] " + ex.Message); }
        }
    }
}
