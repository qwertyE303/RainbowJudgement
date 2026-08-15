using System;
using HarmonyLib;

namespace RainbowJudgement
{
    /// <summary>新关卡开始（scrController.Awake，场景加载新建实例）时重置统计；
    /// 经过存档点/重开（Start_Rewind）不清零——仅完全通过关卡后进入下一关才清零</summary>
    [HarmonyPatch(typeof(scrController), "Awake")]
    public static class LevelResetPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                RainbowState.Reset();
                JudgementHistory.Clear();
				RainbowCounter.Reset();
                MeterVisualPatch.RefreshAllMeters();
				FlawlessXOverlay.Hide();
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowJudgement] Awake hook 异常: " + ex.Message);
            }
        }
    }

    /// <summary>编辑器从 playtest 切回编辑模式（esc退出游玩状态）时清零计数/统计</summary>
    [HarmonyPatch(typeof(scnEditor), "SwitchToEditMode")]
    public static class EditorExitPlaytestPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                RainbowState.Reset();
                JudgementHistory.Clear();
                RainbowCounter.Reset();
                MeterVisualPatch.RefreshAllMeters();
				FlawlessXOverlay.Hide();
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowJudgement] SwitchToEditMode hook 异常: " + ex.Message);
            }
        }

    /// <summary>回档（Start_Rewind）：按回退目标 seqID 截断判定历史并重算所有统计（参考原版 scrMistakesManager 的 Truncate）。
    /// 不清零——只撤销回档点之后的判定，重打的判定重新累计，计数不重复膨胀。</summary>
    [HarmonyPatch(typeof(scrController), "Start_Rewind")]
    public static class RewindTruncatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(int _currentSeqID)
        {
            try
            {
                JudgementHistory.Truncate(_currentSeqID);
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowJudgement] Truncate 异常: " + ex.Message);
            }
        }
    }
    }
}