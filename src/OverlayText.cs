using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RainbowJudgement
{
    /// <summary>
    /// 关卡内浮层文本的通用设施：一个独立的 ScreenSpaceOverlay Canvas（sortingOrder 999、跨场景保留）
    /// + 若干 TMP 文本项。显隐/内容的时机由 <see cref="LiveDisplay"/> 决定，这里只负责"建、改、藏"。
    /// </summary>
    public static class OverlayText
    {
        private static GameObject _canvas;
        private static Transform _root;
        private static bool _failed;

        /// <summary>确保 Canvas 已创建（失败只记一次，不反复尝试）</summary>
        public static bool EnsureCanvas()
        {
            if (_root != null) return true;
            if (_failed) return false;
            try
            {
                _canvas = new GameObject("RainbowJudgementOverlay");
                UnityEngine.Object.DontDestroyOnLoad(_canvas);
                Canvas canvas = _canvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 999;
                _canvas.AddComponent<CanvasScaler>();
                _canvas.AddComponent<LiveDisplay.Updater>();
                _root = _canvas.transform;
                Logger.Log("[Overlay] Canvas 已创建");
                return true;
            }
            catch (Exception ex)
            {
                _failed = true;
                Logger.Warn("[Overlay] Canvas 创建失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>创建一个实时文本项（居中锚点；位置/字号由 Apply 设定）</summary>
        public static TextMeshProUGUI Create(string name)
        {
            if (!EnsureCanvas()) return null;
            try
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(_root, false);
                TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
                text.richText = true;
                text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.Center;
                text.color = Color.white;
                text.overflowMode = TextOverflowModes.Overflow;
                text.font = FindFont();
                RemoveOutline(text);
                AddShadow(text);

                RectTransform rect = go.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(1200f, 160f);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                return text;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Overlay] 文本创建失败(" + name + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 同步字号 / 位置 / 对齐；内容没变就不赋值（避免 TMP 每帧重排）。
        /// 对齐靠"文本对齐方式 + 矩形 pivot"一起实现：X 坐标锚在 pivot 上，
        /// 于是左对齐 = 左端固定向右生长、右对齐 = 右端固定向左生长、居中 = 两端对称生长。
        /// </summary>
        public static void Apply(TextMeshProUGUI text, int fontSize, int x, int y, int align)
        {
            if (text == null) return;
            try
            {
                if (text.font == null) text.font = FindFont();
                if (Mathf.Abs(text.fontSize - fontSize) > 0.01f) text.fontSize = fontSize;

                TextAlignmentOptions alignment = align == 0 ? TextAlignmentOptions.Left
                    : (align == 2 ? TextAlignmentOptions.Right : TextAlignmentOptions.Center);
                if (text.alignment != alignment) text.alignment = alignment;

                RectTransform rect = text.rectTransform;
                if (rect != null)
                {
                    float pivotX = align == 0 ? 0f : (align == 2 ? 1f : 0.5f);
                    if (rect.pivot.x != pivotX) rect.pivot = new Vector2(pivotX, rect.pivot.y);
                    Vector2 position = new Vector2(x, y);
                    if (rect.anchoredPosition != position) rect.anchoredPosition = position;
                }
            }
            catch { }
        }

        public static void SetText(TextMeshProUGUI text, string content, ref string cache)
        {
            if (text == null) return;
            if (content == cache) return;
            cache = content;
            text.text = content;
        }

        public static void Show(TextMeshProUGUI text, bool visible)
        {
            if (text == null) return;
            if (text.gameObject.activeSelf != visible) text.gameObject.SetActive(visible);
        }

        // ---------------- 字体 / 材质 ----------------

        /// <summary>TMP 字体：全局默认 → 已加载字体资源 → 场景内存活 TMP 文本</summary>
        public static TMP_FontAsset FindFont()
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
        private static void RemoveOutline(TextMeshProUGUI text)
        {
            try
            {
                Material source = text.fontMaterial;
                if (source == null) return;
                Material material = new Material(source);
                if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", 0f);
                if (material.HasProperty("_OutlineColor")) material.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 0f));
                if (material.HasProperty("_UnderlayColor")) material.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0f));
                if (material.HasProperty("_UnderlaySoftness")) material.SetFloat("_UnderlaySoftness", 0f);
                text.fontMaterial = material;
            }
            catch { }
        }

        /// <summary>右下角阴影（TMP Underlay）</summary>
        private static void AddShadow(TextMeshProUGUI text)
        {
            try
            {
                Material source = text.fontMaterial;
                if (source == null) return;
                Material material = new Material(source);
                if (material.HasProperty("_UnderlayColor")) material.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.225f));
                if (material.HasProperty("_UnderlayOffsetX")) material.SetFloat("_UnderlayOffsetX", 0.75f);
                if (material.HasProperty("_UnderlayOffsetY")) material.SetFloat("_UnderlayOffsetY", -0.75f);
                if (material.HasProperty("_UnderlaySoftness")) material.SetFloat("_UnderlaySoftness", 0f);
                text.fontMaterial = material;
            }
            catch { }
        }
    }
}
