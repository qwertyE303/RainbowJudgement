using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RainbowJudgement
{
    /// <summary>
    /// X^n 的上标渲染：在 txtCongrats 下建一个 TMP 子文本显示 "&lt;sup&gt;n&lt;/sup&gt;"（游戏本体是 UI.Text，不支持上标）。
    /// 定位方式：按"X 完美无瑕"的估算宽度把上标放到 X 与空格之后的右上角。
    /// 生命周期：随 txtCongrats 销毁；新关卡/编辑器退出时主动 Hide。
    /// </summary>
    public static class FlawlessXOverlay
    {
        private static TextMeshProUGUI _overlay;
        private static string _exponent = "";

        public static void Show(scrController controller, string exponent, string colorHex)
        {
            Hide();
            _exponent = exponent;
            try
            {
                if (controller == null || controller.txtCongrats == null) return;
                if (string.IsNullOrEmpty(controller.txtCongrats.text)) return;

                GameObject go = new GameObject("RainbowXPower");
                go.transform.SetParent(controller.txtCongrats.transform, false);
                _overlay = go.AddComponent<TextMeshProUGUI>();
                _overlay.fontSize = 0.65f * controller.txtCongrats.fontSize;
                _overlay.alignment = TextAlignmentOptions.Left;
                _overlay.raycastTarget = false;

                TMP_FontAsset font = FindFont(controller.txtCongrats.font);
                if (font != null)
                {
                    Apply(font, exponent, colorHex, controller);
                }
                else
                {
                    if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] 字体获取失败，启动延迟重试");
                    go.AddComponent<RetryRunner>().Init(controller, exponent, colorHex);
                }

                // 兜底：只要父文本不再是"完美无瑕"，自己销毁（编辑器/自定义关原地重开会复用 txtCongrats）
                string flawless = RDString.Get("status.allPurePerfect", null);
                if (!string.IsNullOrEmpty(flawless)) go.AddComponent<AutoHide>().Init(controller.txtCongrats, flawless);
            }
            catch (Exception ex)
            {
                if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] " + ex.Message);
                Hide();
            }
        }

        public static void Hide()
        {
            if (_overlay == null) return;
            UnityEngine.Object.Destroy(_overlay.gameObject);
            _overlay = null;
        }

        private static void Apply(TMP_FontAsset font, string exponent, string colorHex, scrController controller)
        {
            try
            {
                if (_overlay == null) return;
                _overlay.font = font;
                _overlay.text = "<sup>" + exponent + "</sup>";
                _overlay.color = Spectrum.ParseHex(colorHex, new Color32(95, 255, 78, 255));

                // 阴影减半（实例化材质，不影响游戏共享字体材质）
                try
                {
                    Material material = new Material(_overlay.fontSharedMaterial);
                    material.SetFloat("_UnderlayOffsetX", 2f);
                    material.SetFloat("_UnderlayOffsetY", -2f);
                    material.SetFloat("_UnderlaySoftness", 0.2f);
                    Color underlay = material.GetColor("_UnderlayColor");
                    underlay.a *= 0.3f;
                    material.SetColor("_UnderlayColor", underlay);
                    _overlay.fontSharedMaterial = material;
                }
                catch { }

                Position(controller);
                if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] 上标已显示: " + exponent);
            }
            catch (Exception ex)
            {
                if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] Apply: " + ex.Message);
                Hide();
            }
        }

        private static void Position(scrController controller)
        {
            try
            {
                var text = controller.txtCongrats;
                float fontSize = text.fontSize;
                string flawless = RDString.Get("status.allPurePerfect", null);
                if (string.IsNullOrEmpty(flawless)) flawless = "完美无瑕！";

                // 估算文本总宽：X(0.6fs) + 空格(0.3fs) + flawless（CJK=1fs，其他=0.6fs）
                float width = 0.9f * fontSize;
                foreach (char ch in flawless) width += (ch >= 0x2E80 ? 1.0f : 0.6f) * fontSize;

                // 居中文本：空格中心 = -总宽/2 + X宽 + 空格半宽（实测修正：再右移 1 字符并回退 0.1fs）
                float x = -width * 0.5f + 1.55f * fontSize;
                float y = _exponent == "∞" ? 0.35f * fontSize : 0.25f * fontSize;

                RectTransform rt = (RectTransform)_overlay.transform;
                rt.pivot = new Vector2(0.5f, 1f); // 顶中：上标中心落在空格右上角
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(x, y);

                if (Main.Settings.DebugLog)
                    Logger.Log("[FlawlessXOverlay] pos=(" + x.ToString("F1") + "," + y.ToString("F1") + ") estW=" + width.ToString("F1")
                        + " fs=" + fontSize + " flawless=" + flawless + " align=" + text.alignment
                        + " anchor=" + text.rectTransform.anchorMin + " pivot=" + text.rectTransform.pivot
                        + " anchored=" + text.rectTransform.anchoredPosition + " rect=" + text.rectTransform.rect);
            }
            catch (Exception ex)
            {
                if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] Position: " + ex.Message);
            }
        }

        /// <summary>字体获取：TMP 默认字体 → 场景内已加载字体 → 存活 TMP 组件 → 用游戏字体动态创建</summary>
        private static TMP_FontAsset FindFont(Font fallbackFont)
        {
            try
            {
                if (TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;

                TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts != null && fonts.Length > 0) return fonts[0];

                TextMeshProUGUI[] texts = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
                if (texts != null && texts.Length > 0 && texts[0].font != null) return texts[0].font;

                if (fallbackFont != null)
                {
                    TMP_FontAsset created = TMP_FontAsset.CreateFontAsset(fallbackFont.name, "Normal", 90);
                    if (created != null) return created;
                }
            }
            catch (Exception ex)
            {
                if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] FindFont: " + ex.Message);
            }
            return null;
        }

        /// <summary>兜底守卫：父文本不再是"完美无瑕"时自我销毁，避免残留到下一局</summary>
        private class AutoHide : MonoBehaviour
        {
            private Text _target;
            private string _flawless;

            public void Init(Text target, string flawless)
            {
                _target = target;
                _flawless = flawless;
            }

            private void Update()
            {
                try
                {
                    if (_target == null || string.IsNullOrEmpty(_flawless)) { Destroy(gameObject); return; }
                    string text = _target.text;
                    // 文本被临时清空时不销毁（可能只是过场），只有确定换成别的文案才销毁
                    if (!string.IsNullOrEmpty(text) && !text.Contains(_flawless)) Destroy(gameObject);
                }
                catch { Destroy(gameObject); }
            }
        }

        /// <summary>字体还没就绪时的短时重试</summary>
        private class RetryRunner : MonoBehaviour
        {
            private scrController _controller;
            private string _exponent;
            private string _colorHex;
            private float _elapsed;

            public void Init(scrController controller, string exponent, string colorHex)
            {
                _controller = controller;
                _exponent = exponent;
                _colorHex = colorHex;
            }

            private void Update()
            {
                try
                {
                    _elapsed += Time.deltaTime;
                    if (_elapsed > 3f)
                    {
                        if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] 重试超时放弃");
                        Destroy(gameObject);
                        return;
                    }
                    if (_controller == null || _controller.txtCongrats == null) { Destroy(gameObject); return; }

                    TMP_FontAsset font = FindFont(_controller.txtCongrats.font);
                    if (font != null)
                    {
                        Apply(font, _exponent, _colorHex, _controller);
                        Destroy(gameObject);
                    }
                }
                catch { Destroy(gameObject); }
            }
        }
    }
}
