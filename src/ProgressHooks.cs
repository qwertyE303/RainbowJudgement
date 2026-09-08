using System;
using HarmonyLib;

namespace RainbowJudgement
{
    /// <summary>
    /// 进度同步 hooks：把 Mod 的账本挂到游戏自己的进度机制上（保存点 / 回档 / 清零 / 续关 / 丢弃进度），
    /// 正常游玩里不再依赖 scrController.Start_Rewind（它只在关卡开始被调用，参数恒为 -1）。
    /// </summary>
    public static class ProgressHooks
    {
        /// <summary>场景加载（scrController.Awake）：换关/新关卡清零，续关与死亡重开则与游戏对齐</summary>
        [HarmonyPatch(typeof(scrController), "Awake")]
        public static class SceneAwakePatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                Logger.Guard("SceneAwake", delegate
                {
                    RainbowProgress.OnSceneLoaded();
                    MeterVisualPatch.RefreshAllMeters();
                    FlawlessXOverlay.Hide();
                });
            }
        }

        /// <summary>回档：游戏把 hitMargins 弹回存档点后，我们按同样的条数截断并整体重放</summary>
        [HarmonyPatch(typeof(scrMistakesManager), "RevertToLastCheckpoint")]
        public static class MistakesRevertPatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
                Logger.Guard("RainbowProgress/Revert", delegate { RainbowProgress.OnGameRevert(); });
            }
        }

        /// <summary>游戏从头开始（scrController.Start 且 checkpointNum==0 / 编辑器切回编辑模式）→ 清零</summary>
        [HarmonyPatch(typeof(scrMistakesManager), "Reset")]
        public static class MistakesResetPatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
                Logger.Guard("RainbowProgress/Reset", delegate { RainbowProgress.OnGameReset(); });
            }
        }

        /// <summary>游戏保存关卡进度（死亡 / 退出菜单）→ 同步保存我们自己的每格判定数据。
        /// 挂在 Persistence.SetSavedProgress 上而不是 SaveCheckpointProgress：后者会在
        /// checkpointNum==0 / 空列表 / coop 时提前返回、根本没写存档，那时我们若照写就会与游戏存档脱节。</summary>
        [HarmonyPatch(typeof(Persistence), "SetSavedProgress")]
        public static class SavedProgressStorePatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
                Logger.Guard("RainbowProgress/Save", delegate { ProgressStore.Save(); });
            }
        }

        /// <summary>续关读档：游戏恢复 hitMargins 后，把自有数据按前缀对齐恢复</summary>
        [HarmonyPatch(typeof(scrMistakesManager), "LoadCheckpointProgress")]
        public static class CheckpointLoadPatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
                Logger.Guard("RainbowProgress/Load", delegate { ProgressStore.LoadAndAlign(); });
            }
        }

        /// <summary>游戏丢弃关卡进度（通关 / 重开 / 练习跳转）→ 删除我们的进度文件</summary>
        [HarmonyPatch(typeof(Persistence), "DeleteSavedProgress")]
        public static class SavedProgressDeletePatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                Logger.Guard("RainbowProgress/Delete", delegate { ProgressStore.DeleteFile(); });
            }
        }
    }
}
