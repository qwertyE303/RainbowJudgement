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

        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Settings = UnityModManager.ModSettings.Load<RainbowSettings>(modEntry);
            ModPath = modEntry.Path;

            _harmony = new Harmony(modEntry.Info.Id);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = SettingsGui.Draw;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUnload = OnUnload;

            Enabled = true;
            RainbowProgress.Clear();
            Logger.Guard("Main/Load", delegate { CounterDisplay.EnsureUI(); });
            Logger.Guard("Main/Load", delegate
            {
                Logger.Log("[Main] ModPath=" + ModPath + " | 资源基准目录=" + ModPaths.BaseDir);
            });
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            if (!value)
            {
                Settings.ShowAverageJudgment = false;
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
