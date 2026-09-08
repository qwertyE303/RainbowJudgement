using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RainbowJudgement
{
    /// <summary>
    /// 计数器显示：独立 Canvas + TMP 文本，显示 7 个数字（F A B C D E G）。
    /// 只在关卡世界且未暂停时显示；文本内容取自 RainbowCounter（数据源），本类只负责渲染。
    /// </summary>
    public static class CounterDisplay
    {
        private static GameObject _canvas;
        private static TextMeshProUGUI _text;
        private static RectTransform _rect;
        private static int[] _lastCounts;
        private static int _lastSpacing = -1;

        // ---------------- 创建 ----------------

        public static void EnsureUI()
        {
            if (_text != null) return;
            try
            {
                _canvas = new GameObject("RainbowCounterCanvas");
                UnityEngine.Object.DontDestroyOnLoad(_canvas);
                Canvas canvas = _canvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 999;
                _canvas.AddComponent<CanvasScaler>();
                _canvas.AddComponent<Updater>();

                GameObject textObject = new GameObject("CounterText");
                textObject.transform.SetParent(_canvas.transform, false);
                _text = textObject.AddComponent<TextMeshProUGUI>();
                _text.richText = true;
                _text.raycastTarget = false;
                _text.alignment = TextAlignmentOptions.Center;
                _text.color = Color.white;
                _text.overflowMode = TextOverflowModes.Overflow;
                _text.font = FindFont();
                Logger.Log("[RainbowCounter] TMP font = " + (_text.font == null ? "NULL" : _text.font.name)
                    + " | size = " + Main.Settings.CounterFontSize + " | pos = (" + Main.Settings.CounterX + "," + Main.Settings.CounterY + ")");
                _text.fontSize = Main.Settings.CounterFontSize;
                RemoveOutline();
                AddShadow();

                _rect = textObject.GetComponent<RectTransform>();
                _rect.sizeDelta = new Vector2(1000f, 160f);
                _rect.anchorMin = new Vector2(0.5f, 0.5f);
                _rect.anchorMax = new Vector2(0.5f, 0.5f);
                _rect.pivot = new Vector2(0.5f, 0.5f);
                _rect.anchoredPosition = new Vector2(Main.Settings.CounterX, Main.Settings.CounterY);
                Logger.Log("[RainbowCounter] UI 已创建, active=" + _text.gameObject.activeSelf);
            }
            catch (Exception ex)
            {
                Logger.Log("[RainbowCounter] UI 创建失败: " + ex.Message);
            }
        }

        // ---------------- 显示 / 隐藏 ----------------

        public static void Hide()
        {
            if (_text != null && _text.gameObject.activeSelf) _text.gameObject.SetActive(false);
        }

        public static void Refresh()
        {
            if (!Main.Enabled || !Main.Settings.EnableRainbow || !Main.Settings.ShowRainbowCounter)
            {
                Hide();
                return;
            }
            EnsureUI();
            if (_text == null) return;

            if (_text.font == null)
            {
                TMP_FontAsset font = FindFont();
                if (font != null) { _text.font = font; Logger.Log("[RainbowCounter] TMP字体已获取: " + font.name); }
            }
            if (!_text.gameObject.activeSelf) _text.gameObject.SetActive(true);

            int spacing = Mathf.Max(0, Main.Settings.CounterSpacing);
            int[] counts = { RainbowCounter.GreenEarly, RainbowCounter.BlueEarly, RainbowCounter.CyanEarly,
                             RainbowCounter.Purple, RainbowCounter.CyanLate, RainbowCounter.BlueLate, RainbowCounter.GreenLate };
            if (Same(counts, _lastCounts) && spacing == _lastSpacing) return;
            _lastCounts = counts;
            _lastSpacing = spacing;

            string gap = new string(' ', spacing);
            string green = RainbowCounter.PerfectGreenHex;
            _text.text = string.Format(
                "<color=#{0}>{1}</color>" + gap + "<color=#006179>{2}</color>" + gap + "<color=#000067>{3}</color>" + gap
                + "<color=#390055>{4}</color>" + gap + "<color=#000067>{5}</color>" + gap + "<color=#006179>{6}</color>" + gap
                + "<color=#{0}>{7}</color>",
                green, counts[0], counts[1], counts[2], counts[3], counts[4], counts[5], counts[6]);
        }

        /// <summary>把设置里的字号/位置同步到已存在的 UI（拖动滑条即时生效）</summary>
        public static void ApplySettings()
        {
            if (_text == null) return;
            if (Mathf.Abs(_text.fontSize - Main.Settings.CounterFontSize) > 0.01f)
                _text.fontSize = Main.Settings.CounterFontSize;
            if (_rect != null)
            {
                Vector2 position = new Vector2(Main.Settings.CounterX, Main.Settings.CounterY);
                if (_rect.anchoredPosition != position) _rect.anchoredPosition = position;
            }
        }

        private static bool Same(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ---------------- 字体 / 材质 ----------------

        /// <summary>TMP 字体：全局默认 → 已加载字体资源 → 场景内存活 TMP 文本</summary>
        private static TMP_FontAsset FindFont()
        {
            try
            {
                if (TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;

                TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts != null && fonts.Length > 0) return fonts[0];

                TextMeshProUGUI[] texts = UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] != null && texts[i].font != null) return texts[i].font;
                }
            }
            catch { }
            return null;
        }

        /// <summary>去掉字体描边（复制材质修改，不影响游戏其他文字）</summary>
        private static void RemoveOutline()
        {
            try
            {
                if (_text == null) return;
                Material source = _text.fontMaterial;
                if (source == null) return;
                Material material = new Material(source);
                if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", 0f);
                if (material.HasProperty("_OutlineColor")) material.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 0f));
                if (material.HasProperty("_UnderlayColor")) material.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0f));
                if (material.HasProperty("_UnderlaySoftness")) material.SetFloat("_UnderlaySoftness", 0f);
                _text.fontMaterial = material;
            }
            catch { }
        }

        /// <summary>右下角阴影（TMP Underlay）</summary>
        private static void AddShadow()
        {
            try
            {
                if (_text == null) return;
                Material source = _text.fontMaterial;
                if (source == null) return;
                Material material = new Material(source);
                if (material.HasProperty("_UnderlayColor")) material.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.225f));
                if (material.HasProperty("_UnderlayOffsetX")) material.SetFloat("_UnderlayOffsetX", 0.75f);
                if (material.HasProperty("_UnderlayOffsetY")) material.SetFloat("_UnderlayOffsetY", -0.75f);
                if (material.HasProperty("_UnderlaySoftness")) material.SetFloat("_UnderlaySoftness", 0f);
                _text.fontMaterial = material;
            }
            catch { }
        }

        /// <summary>挂在 Canvas 上的更新器：仅关卡世界且未暂停时显示</summary>
        public class Updater : MonoBehaviour
        {
            private void Update()
            {
                if (!GameState.InGameWorld)
                {
                    Hide();
                    return;
                }
                ApplySettings();
                Refresh();
            }
        }

        /// <summary>编辑器从测试切回编辑模式时立即隐藏计数</summary>
        [HarmonyPatch(typeof(scnEditor), "SwitchToEditMode")]
        public static class EditorHidePatch
        {
            public static void Postfix()
            {
                try { Hide(); }
                catch { }
            }
        }
    }
}
