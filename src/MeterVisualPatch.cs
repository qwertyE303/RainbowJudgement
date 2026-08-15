using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RainbowJudgement
{
    /// <summary>
    /// 彩虹仪表盘背景：按用户实测几何生成渐变纹理（横条 / 弧形），替换原版背景。
    /// 弧形: 圆心(200,197)，大弧=黑色阴影(r 116~181, ±66°)，小弧=彩色(r 144~157 宽13px, ±64°)
    /// 直线: 粗线=黑色阴影(329x65，整体左移1px/上移1px)，细线=彩色(314x11)，彩色不透明度100%
    /// 颜色: 中心=完美=深紫380nm -> 渐变 -> 红(255,0,0)700nm；边缘(弧形61°~64°/直线两侧)恒定红；失误也是红
    /// 边缘: 彩色->黑色 0.75px 过渡（RGB 渐变为黑 + alpha 100%->33%）；黑色->透明 1.5px 渐变（不变）
    /// </summary>
    [HarmonyPatch(typeof(scrHitErrorMeter), "UpdateLayout")]
    public static class MeterVisualPatch
    {
        private const int TexW = 400;
        private const int TexH = 200;

        private static Texture2D _straightTex;
        private static Texture2D _curvedTex;
        private static Sprite _straightSprite;
        private static Sprite _curvedSprite;

        private static readonly Dictionary<scrHitErrorMeter, Sprite> _origStraight = new Dictionary<scrHitErrorMeter, Sprite>();
        private static readonly Dictionary<scrHitErrorMeter, Sprite> _origCurved = new Dictionary<scrHitErrorMeter, Sprite>();

        [HarmonyPostfix]
        public static void UpdateLayoutPostfix(scrHitErrorMeter __instance,
            ErrorMeterSize size = ErrorMeterSize.Normal,
            ErrorMeterShape shape = ErrorMeterShape.Straight)
        {
            try
            {
                if (__instance == null) return;
                if (!Main.Enabled || !Main.Settings.EnableRainbow)
                {
                    RestoreMeter(__instance);
                    return;
                }
                EnsureTextures();
                CaptureOriginals(__instance);
                if (__instance.straightMeter != null && _straightSprite != null)
                    ReplaceRootImageOnly(__instance.straightMeter, _straightSprite);
                if (__instance.curvedMeter != null && _curvedSprite != null)
                    ReplaceRootImageOnly(__instance.curvedMeter, _curvedSprite);
            }
            catch (Exception ex)
            {
                Logger.Warn("[RainbowJudgement] UpdateLayout hook 异常: " + ex.Message);
            }
        }

        public static void RefreshAllMeters()
        {
            try
            {
                scrHitErrorMeter[] meters = UnityEngine.Object.FindObjectsByType<scrHitErrorMeter>(FindObjectsSortMode.None);
                if (meters == null) return;
                foreach (var m in meters)
                {
                    if (m == null) continue;
                    try { UpdateLayoutPostfix(m); }
                    catch { }
                }
            }
            catch { }
        }

        public static void RestoreAllMeters()
        {
            try
            {
                scrHitErrorMeter[] meters = UnityEngine.Object.FindObjectsByType<scrHitErrorMeter>(FindObjectsSortMode.None);
                if (meters == null) return;
                foreach (var m in meters)
                {
                    if (m == null) continue;
                    try { RestoreMeter(m); }
                    catch { }
                }
            }
            catch { }
        }

        private static void RestoreMeter(scrHitErrorMeter meter)
        {
            Sprite s;
            if (meter.straightMeter != null && _origStraight.TryGetValue(meter, out s) && s != null)
            {
                ReplaceRootImageOnly(meter.straightMeter, s);
                _origStraight.Remove(meter); // 恢复后移除，防实例残留
            }
            if (meter.curvedMeter != null && _origCurved.TryGetValue(meter, out s) && s != null)
            {
                ReplaceRootImageOnly(meter.curvedMeter, s);
                _origCurved.Remove(meter); // 恢复后移除，防实例残留
            }
        }

        private static void CaptureOriginals(scrHitErrorMeter meter)
        {
            if (meter.straightMeter != null)
            {
                Image img = meter.straightMeter.GetComponent<Image>();
                if (img != null && img.sprite != null && img.sprite != _straightSprite)
                {
                    if (!_origStraight.ContainsKey(meter)) _origStraight[meter] = img.sprite;
                }
            }
            if (meter.curvedMeter != null)
            {
                Image img = meter.curvedMeter.GetComponent<Image>();
                if (img != null && img.sprite != null && img.sprite != _curvedSprite)
                {
                    if (!_origCurved.ContainsKey(meter)) _origCurved[meter] = img.sprite;
                }
            }
        }

        private static void ReplaceRootImageOnly(GameObject root, Sprite sprite)
        {
            if (root == null || sprite == null) return;
            Image img = root.GetComponent<Image>();
            if (img != null)
                img.sprite = sprite;
        }

        // ---------------- 纹理生成 ----------------

        private static void EnsureTextures()
        {
            if (_straightTex != null && _curvedTex != null) return;
            if (_straightTex == null)
            {
                // 优先加载用户自定义 PNG（Mods\RainbowJudgement\ 下），不存在则代码生成
                _straightTex = LoadPng("straight_meter.png");
                if (_straightTex == null) _straightTex = BuildStraightTexture();
                _straightSprite = MakeSprite(_straightTex);
            }
            if (_curvedTex == null)
            {
                _curvedTex = LoadPng("curved_meter.png");
                if (_curvedTex == null) _curvedTex = BuildCurvedTexture();
                _curvedSprite = MakeSprite(_curvedTex);
            }
        }

        /// <summary>从 mod 目录加载用户自定义 PNG（400x200，RGBA），失败返回 null 回退代码生成</summary>
        private static Texture2D LoadPng(string fileName)
        {
            try
            {
                string path = System.IO.Path.Combine(Main.ModPath, fileName);
                if (!System.IO.File.Exists(path)) return null;
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                if (tex.LoadImage(bytes))
                {
                    Logger.Log("[MeterVisualPatch] 已加载自定义仪表盘图片: " + path);
                    return tex;
                }
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log("[MeterVisualPatch] 加载自定义图片失败，回退代码生成: " + ex.Message);
                return null;
            }
        }

        /// <summary>角度/位置归一化 t（0=中心完美 -> 1=红）-> 颜色（alpha 100%）</summary>
        private static Color32 ColorFromT(float t)
        {
            double lambda = Spectrum.MinWavelengthNm + (RainbowMath.RedWavelengthNm - Spectrum.MinWavelengthNm) * t;
            return Spectrum.WavelengthToRgb(lambda);
        }

        /// <summary>边缘透明度渐变：edge 为到边界的距离(px)，<1.5px 时按比例降低 alpha</summary>
        private static byte EdgeAlpha(float edge, int baseAlpha)
        {
            if (edge >= 1.5f) return (byte)baseAlpha;
            if (edge <= 0f) return 0;
            return (byte)(baseAlpha * (edge / 1.5f));
        }

        /// <summary>
        /// 彩色边缘过渡：RGB 从黑(0,0,0)过渡到彩色，透明度从 33%(84) 过渡到 100%(255)，
        /// 过渡宽度由调用方指定（直线左右竖边 1.5px，弧形 0.75px）。
        /// 黑色到透明的阴影边缘过渡不动（见 EdgeAlpha）。
        /// </summary>
        private static Color32 EdgeColorToShadow(Color32 color, float edge, float width)
        {
            if (edge >= width) return color;
            if (edge <= 0f) return new Color32(0, 0, 0, 84);
            return Color32.Lerp(new Color32(0, 0, 0, 84), color, edge / width);
        }

        /// <summary>生成横条渐变：粗线黑色阴影(65px) + 细线彩色(11px)，对称居中，边缘渐变</summary>
        private static Texture2D BuildStraightTexture()
        {
            Texture2D tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            // 用户实测参数（视觉坐标，y=0 顶部）——整体左移1px(x-1)、上移1px(y-1)
            // 粗线阴影: 底部 y 66~131，x 35~363
            const int shadowY0 = 66, shadowY1 = 131;
            const int shadowX0 = 34, shadowX1 = 365;
            // 细线彩色: 底部 y 94~105，x 42~356
            const int colorY0 = 94, colorY1 = 105;
            const int colorX0 = 41, colorX1 = 358;
            // 恒定红边界（左移后）：x<48 与 x>349
            const int redLeft = 48;
            const int redRight = 351;

            Color32 shadowBase = new Color32(0, 0, 0, 84);
            Color32 transparent = new Color32(0, 0, 0, 0);
            Color32[] px = new Color32[TexW * TexH];

            for (int vy = 0; vy < TexH; vy++)
            {
                for (int x = 0; x < TexW; x++)
                {
                    Color32 col = transparent;
                    if (x >= shadowX0 && x <= shadowX1 && vy >= shadowY0 && vy <= shadowY1)
                    {
                        // 阴影粗线（黑色33%，边缘3px渐变）
                        float edge = Mathf.Min(Mathf.Min(vy - shadowY0, shadowY1 - vy), Mathf.Min(x - shadowX0, shadowX1 - x));
                        col = shadowBase;
                        col.a = EdgeAlpha(edge, 84);
                    }
                    if (x >= colorX0 && x <= colorX1 && vy >= colorY0 && vy <= colorY1)
                    {
                        // 彩色细线（alpha 100%，仅左右边缘 0.75px 过渡，上下为硬边）
                        float edge = Mathf.Min(x - colorX0, colorX1 - x);
                        if (x < redLeft || x > redRight)
                        {
                            col = RainbowMath.RedColor;
                        }
                        else
                        {
                            // 渐变：以彩色区域中心 199.5 为对称轴，左右各 151.5px（红区 7px 对称）
                            float t;
                            if (x <= 199)
                                t = (199.5f - x) / 151.5f;
                            else
                                t = (x - 199.5f) / 151.5f;
                            col = ColorFromT(t);
                        }
                        
                        col = EdgeColorToShadow(col, edge, 1.5f);
                    }
                    // Unity 纹理 y=0 为底部，翻转行序
                    px[(TexH - 1 - vy) * TexW + x] = col;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>生成弧形渐变：大弧黑色阴影 + 小弧彩色(宽12px)，顶部中心深紫向两侧渐变，61°~64°恒定红，边缘渐变</summary>
        private static Texture2D BuildCurvedTexture()
        {
            Texture2D tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            // 用户实测参数（视觉坐标，y=0 顶部）
            const float cx = 199f, cy = 198f;
            // 大弧(阴影): 内弧距圆心116，宽65 -> r 116~181；张角 ±67°
            const float shadowRIn = 116f, shadowROut = 181f, shadowAng = 67f;
            // 小弧(彩色): 内弧距圆心145，宽12 -> r 145~157；张角 ±64°
            const float colorRIn = 145f, colorROut = 157f, colorAng = 64f;
            // 恒定红: 61°~64°
            const float redStartAng = 61f;

            Color32 shadowBase = new Color32(0, 0, 0, 84);
            Color32 transparent = new Color32(0, 0, 0, 0);
            Color32[] px = new Color32[TexW * TexH];

            for (int vy = 0; vy < TexH; vy++)
            {
                for (int x = 0; x < TexW; x++)
                {
                    float dx = x - cx;
                    float dy = vy - cy;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float theta = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;

                    Color32 col = transparent;
                    // 大弧阴影（边缘3px渐变：径向+角度向）
                    if (r >= shadowRIn && r <= shadowROut && theta >= -shadowAng && theta <= shadowAng)
                    {
                        float edgeR = Mathf.Min(r - shadowRIn, shadowROut - r);
                        float edgeA = (shadowAng - Mathf.Abs(theta)) * Mathf.Deg2Rad * r;
                        col = shadowBase;
                        col.a = EdgeAlpha(Mathf.Min(edgeR, edgeA), 84);
                    }
                    // 小弧彩色（alpha 100%，边缘0.75px过渡）
                    if (r >= colorRIn && r <= colorROut && theta >= -colorAng && theta <= colorAng)
                    {
                        float abs = Mathf.Abs(theta);
                        if (abs >= redStartAng)
                            col = RainbowMath.RedColor;
                        else
                            col = ColorFromT(abs / redStartAng);
                        float edgeR = Mathf.Min(r - colorRIn, colorROut - r);
                        float edgeA = (colorAng - abs) * Mathf.Deg2Rad * r;
                        col = EdgeColorToShadow(col, Mathf.Min(edgeR, edgeA), 0.75f);
                    }
                    px[(TexH - 1 - vy) * TexW + x] = col;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private static Sprite MakeSprite(Texture2D tex)
        {
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
