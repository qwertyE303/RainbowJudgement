using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace RainbowJudgement
{
    /// <summary>Mod 入口：加载补丁、装配设置界面、处理开关/卸载</summary>
    public static class Main
    {
        public static RainbowSettings Settings;
        public static string ModPath;
        public static bool Enabled { get; private set; }

        /// <summary>
        /// 所有补丁的统一总闸门：Mod 已加载 **且** 玩家开着「开启彩虹判定」。
        /// 收敛原先散落在各 hook 里的 `!Main.Enabled || !Main.Settings.EnableRainbow`
        /// （写成属性顺带把 Settings 的空引用也兜住了）。语义与逐处判断完全一致。
        /// </summary>
        public static bool Active
        {
            get
            {
                if (!Enabled) return false;
                RainbowSettings settings = Settings;
                return settings != null && settings.EnableRainbow;
            }
        }

        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Settings = UnityModManager.ModSettings.Load<RainbowSettings>(modEntry);
            ModPath = modEntry.Path;
            Logger.Init(); // 定位 Mod 目录并清空 <RJ Mod 目录>\Log.txt（每次启动游戏重记）

            CustomJudge.EnsureDefaults(); // 首次运行：写入出厂四条自定义判定
            CustomJudge.InvalidateLayout();

            _harmony = new Harmony(modEntry.Info.Id);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = SettingsGui.Draw;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUnload = OnUnload;

            Enabled = true;
            RainbowProgress.Clear();
            Logger.Guard("Main/Load", delegate { LiveDisplay.EnsureUI(); });
            Logger.Guard("Main/Load", delegate
            {
                Logger.Log("[Main] UMM ModPath=" + ModPath + " | 资源基准目录=" + ModPaths.BaseDir
                    + " | 日志文件=" + Logger.FilePath
                    + " | 语言=" + Lang.Resolve()
                    + " | 开关 彩虹=" + Settings.EnableRainbow + " 判定详情=" + Settings.ShowJudgeDetails
                    + "（时间=" + Settings.ShowAverageTime + " 颜色=" + Settings.ShowAverageColor
                    + " 角度=" + Settings.ShowAverageAngle + "）"
                    + " | 实时 颜色=" + Settings.ShowLiveColor + " 时间=" + Settings.ShowLiveTime
                    + " 角度=" + Settings.ShowLiveAngle
                    + " | 自定义判定=" + Settings.EnableCustomJudge + " 档位数=" + CustomJudge.EnabledCount
                    + " 显示计数=" + Settings.ShowCustomCount + " 结尾页计数=" + Settings.ShowCustomCountInResults
                    + " | DebugLog=" + Settings.DebugLog);
            });
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            if (!value)
            {
                Settings.ShowJudgeDetails = false;
                Logger.Guard("Main/Toggle", delegate { MeterVisualPatch.RestoreAllMeters(); });
            }
            else
            {
                // 关闭期间游戏仍在计数 → 重新对齐（缺数据的条目用占位补齐，保证索引不错位）
                Logger.Guard("Main/Toggle", delegate { RainbowProgress.RealignFromGame(); });
                Logger.Guard("Main/Toggle", delegate { MeterVisualPatch.RefreshAllMeters(); });
            }
            return true;
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            Logger.Guard("Main/Unload", delegate
            {
                Enabled = false;
                MeterVisualPatch.RestoreAllMeters();
                LiveDisplay.HideAll();
                if (_harmony != null)
                {
                    _harmony.UnpatchAll(modEntry.Info.Id);
                    _harmony = null;
                }
            });
            return true;
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }
    }
}
