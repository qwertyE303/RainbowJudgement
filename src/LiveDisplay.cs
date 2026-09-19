using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace RainbowJudgement
{
    /// <summary>
    /// 关卡内**实时**判定详情（4 个互相独立的文本项，各自有开关 / 字号 / XY）：
    ///   · 自定义判定计数（2n−1 个数字，按档位着色）
    ///   · 平均颜色 / 平均绝对时间偏差 / 平均绝对角度偏差（文案与结尾页同一套小数规则）
    /// 只在关卡世界且未暂停时显示；文本内容取自账本重放后的聚合值，这里只负责渲染。
    /// </summary>
    public static class LiveDisplay
    {
        private static TextMeshProUGUI _count;
        private static TextMeshProUGUI _color;
        private static TextMeshProUGUI _time;
        private static TextMeshProUGUI _angle;

        private static string _countCache;
        private static string _colorCache;
        private static string _timeCache;
        private static string _angleCache;

        // 计数文本的重建条件：档位定义版本 + 间距 + 各档计数（避免每帧拼字符串）
        private static readonly int[] _snapshot = new int[CustomJudge.MaxTiers * 2];
        private static int _snapshotRev = -1;
        private static int _snapshotSpacing = -1;

        // ---------------- 创建 ----------------

        public static void EnsureUI()
        {
            if (_count != null && _color != null && _time != null && _angle != null) return;
            if (!OverlayText.EnsureCanvas()) return;
            if (_count == null) _count = OverlayText.Create("RJCustomCount");
            if (_color == null) _color = OverlayText.Create("RJAvgColor");
            if (_time == null) _time = OverlayText.Create("RJAvgTime");
            if (_angle == null) _angle = OverlayText.Create("RJAvgAngle");
        }

        // ---------------- 每帧：同步外观 + 内容 ----------------

        public static void Refresh()
        {
            RainbowSettings settings = Main.Settings;
            if (!Main.Enabled || settings == null || !settings.EnableRainbow)
            {
                HideAll();
                return;
            }

            EnsureUI();

            // 自定义判定计数：只要开了就显示（数量为 0 也显示，方便玩家摆位置）
            bool showCount = settings.EnableCustomJudge && settings.ShowCustomCount && CustomJudge.EnabledCount > 0;
            OverlayText.Show(_count, showCount);
            if (showCount) UpdateCountText(settings);

            // 三项平均值与计数**同时**出现：进关卡（含准备界面）就显示，不等第一条判定（此时是 0）
            bool showColor = settings.ShowLiveColor;
            OverlayText.Show(_color, showColor);
            if (showColor) OverlayText.SetText(_color, JudgeDetails.LiveColorText(), ref _colorCache);

            bool showTime = settings.ShowLiveTime;
            OverlayText.Show(_time, showTime);
            if (showTime) OverlayText.SetText(_time, JudgeDetails.LiveTimeText(), ref _timeCache);

            bool showAngle = settings.ShowLiveAngle;
            OverlayText.Show(_angle, showAngle);
            if (showAngle) OverlayText.SetText(_angle, JudgeDetails.LiveAngleText(), ref _angleCache);
        }

        /// <summary>把设置里的字号 / 位置 / 对齐同步到已存在的文本（拖动滑条即时生效）</summary>
        public static void ApplySettings()
        {
            RainbowSettings settings = Main.Settings;
            if (settings == null) return;
            OverlayText.Apply(_count, settings.CustomCountFontSize, settings.CustomCountX, settings.CustomCountY, 1);
            OverlayText.Apply(_color, settings.LiveColorFontSize, settings.LiveColorX, settings.LiveColorY, settings.LiveColorAlign);
            OverlayText.Apply(_time, settings.LiveTimeFontSize, settings.LiveTimeX, settings.LiveTimeY, settings.LiveTimeAlign);
            OverlayText.Apply(_angle, settings.LiveAngleFontSize, settings.LiveAngleX, settings.LiveAngleY, settings.LiveAngleAlign);
        }

        public static void HideAll()
        {
            OverlayText.Show(_count, false);
            OverlayText.Show(_color, false);
            OverlayText.Show(_time, false);
            OverlayText.Show(_angle, false);
        }

        // ---------------- 计数文本 ----------------

        private static void UpdateCountText(RainbowSettings settings)
        {
            int spacing = Mathf.Max(0, settings.CustomCountSpacing);
            if (SameCounts(spacing)) return;
            SaveCounts(spacing);

            int count = JudgeDetails.BuildCustomSequence();
            if (count <= 0)
            {
                OverlayText.SetText(_count, "", ref _countCache);
                return;
            }

            string gap = new string(' ', spacing);
            StringBuilder sb = new StringBuilder(64);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(gap);
                string hex = CustomJudge.DigitHex(CustomJudge.EnabledAt(JudgeDetails.SequenceIndex(i)));
                sb.Append("<color=#").Append(hex).Append(">").Append(JudgeDetails.SequenceValue(i)).Append("</color>");
            }
            OverlayText.SetText(_count, sb.ToString(), ref _countCache);
        }

        private static bool SameCounts(int spacing)
        {
            if (_snapshotRev != CustomJudge.LayoutRevision || _snapshotSpacing != spacing) return false;
            int n = CustomJudge.EnabledCount;
            int k = 0;
            for (int i = 0; i < n && k + 1 < _snapshot.Length; i++)
            {
                if (_snapshot[k++] != CustomCounter.Early[i]) return false;
                if (_snapshot[k++] != CustomCounter.Late[i]) return false;
            }
            return true;
        }

        private static void SaveCounts(int spacing)
        {
            int n = CustomJudge.EnabledCount;
            int k = 0;
            for (int i = 0; i < n && k + 1 < _snapshot.Length; i++)
            {
                _snapshot[k++] = CustomCounter.Early[i];
                _snapshot[k++] = CustomCounter.Late[i];
            }
            _snapshotRev = CustomJudge.LayoutRevision;
            _snapshotSpacing = spacing;
        }

        // ---------------- 时机 ----------------

        /// <summary>挂在 Canvas 上的更新器：仅关卡世界且未暂停时显示</summary>
        public class Updater : MonoBehaviour
        {
            private void Update()
            {
                if (!GameState.InGameWorld)
                {
                    HideAll();
                    return;
                }
                ApplySettings();
                Refresh();
            }
        }

        /// <summary>编辑器从测试切回编辑模式时立即隐藏</summary>
        [HarmonyPatch(typeof(scnEditor), "SwitchToEditMode")]
        public static class EditorHidePatch
        {
            public static void Postfix()
            {
                try { HideAll(); }
                catch { }
            }
        }
    }
}
