using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RainbowJudgement
{
	/// <summary>判定 hooks</summary>
	public static class JudgeHooks
	{
		[HarmonyPatch(typeof(scrHitErrorMeter), "CalculateTickColor")]
		[HarmonyPriority(Priority.High)]
		public static class TickColorHook
		{
			[HarmonyPrefix]
			public static bool Prefix(ref Color __result, float angle, float marginScale, scrFloor hitFloor)
			{
				try
				{
					if (!Main.Enabled || !Main.Settings.EnableRainbow)
						return true;
					// 判定瞬间基准（含 marginScale，与游戏 AddHit 缩放同源）
					double bts = RainbowMath.GameBpmTimesSpeedOf(hitFloor);
					double pitch = RainbowMath.GetPitchNow();
					double countedDeg = scrMisc.GetAdjustedAngleBoundaryInDeg(HitMarginGeneral.Counted, bts, pitch, marginScale);
					if (double.IsNaN(countedDeg) || countedDeg <= 0.0001) countedDeg = 60.0; // NaN防御
					// —— 锚点间线性渐变：位置=缩放刻度，锚点位置=LevelBoundaryDeg×60/countedDeg（与原版判定边界同构），锚点色=ANCHOR_WAVELENGTH固定 ——
					double wl = RainbowMath.WavelengthForGradient(Math.Abs((double)angle), countedDeg, bts, pitch, marginScale);
					__result = Spectrum.WavelengthToRgb(wl);
					if (Main.Settings.DebugLog)
						Logger.Log("[TickColor] angle=" + angle.ToString("F2") + " countedDeg=" + countedDeg.ToString("F1") + " wl=" + wl.ToString("F1") + "nm");
					return false; // 原版判定档位颜色同样被锚点色覆盖（Perfect→540/EP·LP→620/VE·VL·Too→700）
				}
				catch
				{
					return true;
				}
			}
		}

		/// <summary>轻量：记录游戏最终刻度（auto 强制中间→0）。不做统计（AddHit 调用不完整会漏计）</summary>
		[HarmonyPatch(typeof(scrHitErrorMeter), "AddHit")]
		public static class AddHitTickAngleHook
		{
			[HarmonyPostfix]
			public static void Postfix(float angleDiff)
			{
				try
				{
					if (Main.Enabled && Main.Settings.EnableRainbow)
						RainbowCounter.LastTickAngle = Math.Abs(angleDiff);
				}
				catch { }
			}
		}

		/// <summary>检测 auto 判定（官方 autoplay + 自动砖块）：scrPlayer.Hit 是每次判定的必经入口，isAuto 参数直接标明本次判定是否 auto 触发。</summary>
	[HarmonyLib.HarmonyPatch(typeof(scrPlayer), "Hit")]
	public static class AutoDetectPatch
	{
		[HarmonyPrefix]
		public static void Prefix(scrPlayer __instance, bool isAuto)
		{
			try
			{
				RainbowCounter.AutoActive = isAuto || (__instance != null && __instance.auto);
			}
			catch { }
		}
	}

		/// <summary>彩虹计数：统计 PurePerfect（不含 EP/LP），按时间误差分组</summary>
	[HarmonyLib.HarmonyPatch(typeof(scrMisc), "GetHitMargin")]
	public static class GetMarginHook
	{
		public static void Postfix(ref HitMargin __result, float hitangle, float refangle, bool isCW, float bpmTimesSpeed, float conductorPitch, double marginScale)
		{
			if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
			try
			{
				// hitangle/refangle 是弧度 -> 度
				double delta = (hitangle - refangle) * (isCW ? 1.0 : -1.0) * 57.29578;
				if (RainbowCounter.AutoActive) delta = 0.0; // auto判定（官方autoplay/自动砖块）强制完美中心：计数分档/文字颜色全落 Purple
				RainbowCounter.LastDeltaDeg = delta; // 供判定文字颜色使用
				// 判定瞬间基准（含 marginScale）——与游戏 tick 刻度同源，供判定文字颜色使用
				double countedDeg = scrMisc.GetAdjustedAngleBoundaryInDeg(HitMarginGeneral.Counted, bpmTimesSpeed, conductorPitch, marginScale);
				RainbowCounter.LastCountedDeg = countedDeg;
				RainbowCounter.LastScaledPos = Math.Abs(delta) * (double)RainbowMath.CountedScaled / countedDeg;
				RainbowCounter.LastBpmTimesSpeed = bpmTimesSpeed;
				RainbowCounter.LastPitch = conductorPitch;
				RainbowCounter.LastMarginScale = marginScale;
				// 统计不依赖 ShowRainbowCounter（仅 UI 显示受其控制）：X完美无瑕/分组计数始终工作
				// 与原版 GetHitMargin 同构：新增档位边界角度 = max(角度下限×mult, 时间下限对应角度)
				double a1 = RainbowMath.LevelBoundaryDeg(0, bpmTimesSpeed, conductorPitch, marginScale); // 1/3PP
				double a2 = RainbowMath.LevelBoundaryDeg(1, bpmTimesSpeed, conductorPitch, marginScale); // 0.5PP
				double a3 = RainbowMath.LevelBoundaryDeg(2, bpmTimesSpeed, conductorPitch, marginScale); // 2/3PP
				if (Main.Settings.DebugLog)
					Logger.Log("[GetMargin] __result=" + __result + " delta=" + delta.ToString("F1") + "deg a1=" + a1.ToString("F1") + " a2=" + a2.ToString("F1") + " a3=" + a3.ToString("F1") + " scale=" + marginScale.ToString("F2"));
				RainbowCounter.LastWasPerfect = (__result == HitMargin.Perfect) || RainbowCounter.AutoActive;
				if (__result != HitMargin.Perfect && !RainbowCounter.AutoActive)
				{
					RainbowCounter.HasNonPerfect = true; // 记录 PP开外判定（EP/LP/VE/VL/Too）→ 结尾保持原版"恭喜"
					return;
				}
				// —— 完整统计（GetMargin 每次判定都调用；AddHit 调用不完整会漏计，故统计不用 AddHitHook）——
				// 分档：用真实角度差 delta（普通游玩与游戏刻度等价；auto 10倍速下 delta 相对 a1 很小→仍归 Purple）
				int group = RainbowCounter.AddByAngle(Math.Abs(delta), delta < 0.0, a1, a2, a3);
				// X^n 完美度 p：用游戏最终刻度 LastTickAngle 反推角度（auto 强制中间→p=0→n=∞）
				double ppDeg = RainbowMath.LevelBoundaryDeg(3, bpmTimesSpeed, conductorPitch, marginScale);
				double p = 0.0;
				double tickAngle = RainbowCounter.LastTickAngle; // 游戏最终刻度（auto=0）
				if (RainbowCounter.AutoActive) tickAngle = 0.0; // auto：防御强制中心（AddHit 正常传0，双保险）
				if (ppDeg > 0.0001) { p = tickAngle / (double)RainbowMath.CountedScaled * countedDeg / ppDeg; RainbowCounter.AddPerfectP(p); }
				// 平均判定颜色/平均绝对偏差：与 tick 同一套渐变（刻度 = 游戏最终刻度 LastTickAngle）
				double lambda = RainbowMath.WavelengthForGradient(tickAngle, countedDeg, bpmTimesSpeed, conductorPitch, marginScale);
				double timeMs = RainbowMath.TimeMsFromScaledAngleAccurate((float)tickAngle, countedDeg, bpmTimesSpeed, conductorPitch);
				RainbowState.Add(lambda, timeMs);
				// 统一判定历史（回档 Truncate 重算用）
				try
				{
					int seq = 0;
					int seqFloor = -1, seqCtrl = -1, seqFloorID = -1;
					try
					{
						var ctrl = scrController.instance;
						if (ctrl != null)
						{
							seqCtrl = ctrl.currentSeqID;
							seqFloorID = ctrl.currentFloorID;
							if (ctrl.playerOne != null && ctrl.playerOne.currFloor != null)
								seqFloor = ctrl.playerOne.currFloor.seqID;
						}
						seq = seqFloor >= 0 ? seqFloor : (seqCtrl >= 0 ? seqCtrl : seqFloorID); // 按键时 floor seq（会滞后，Truncate 用首次位置截断绕开）
					}
					catch { }
					JudgementHistory.Add(seq, lambda, timeMs, true, group, p);
					if (Main.Settings.DebugLog)
						Logger.Log("[GetMargin] seqFloor=" + seqFloor + " seqCtrl=" + seqCtrl + " floorID=" + seqFloorID + " use=" + seq);
				}
				catch { }
			}
			catch (Exception ex)
			{
				Logger.Log("[JudgeHooks/GetMargin] " + ex.Message);
			}
		}
	}

	/// <summary>判定文字（球上冒出的 Perfect/稍快等）颜色改为彩虹判定对应色（与 tick 同一套位置映射）</summary>
	[HarmonyLib.HarmonyPatch(typeof(scrHitTextMesh), "Show")]
	public static class HitTextColorPatch
	{
		public static void Postfix(scrHitTextMesh __instance)
		{
			if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
			try
			{
				if (__instance == null || __instance.text == null) return;
				// —— 与 tick 完全同一套渐变：LastScaledPos=判定瞬间游戏同款缩放刻度（GetMarginHook 存）——
				double wl = RainbowMath.WavelengthForGradient(
					RainbowCounter.LastScaledPos,
					RainbowCounter.LastCountedDeg,
					RainbowCounter.LastBpmTimesSpeed,
					RainbowCounter.LastPitch,
					RainbowCounter.LastMarginScale);
				Color rainbow = Spectrum.WavelengthToRgb(wl);
				rainbow.a = __instance.text.color.a; // 保留原 alpha（淡出动画控制）
				__instance.text.color = rainbow;
			}
			catch (Exception ex)
			{
				Logger.Log("[HitTextColor] " + ex.Message);
			}
		}
	}

	/// <summary>全 PP（无 PP开外判定）时显示"完美无瑕"，在其前加 X^n（n 为上标，Unicode 上标字符渲染——txtCongrats 是 UI.Text，不支持 <sup>）。
/// n=(1/r)-1，r=各判定 |角度|/PP边界的平均（权重1）；r=0 → n=∞；n<1 保留两位小数、n>=1 三位有效数字。
/// X^n 颜色按档位：存在 PP~2/3PP→540nm；否则存在 2/3PP~0.5PP→487nm；否则存在 0.5PP~1/3PP→460nm；否则 433nm</summary>
	[HarmonyLib.HarmonyPatch(typeof(scrController), "OnLandOnPortal")]
	public static class FlawlessXPatch
	{
		public static void Postfix(scrController __instance)
		{
			try
			{
				if (!Main.Enabled || !Main.Settings.EnableRainbow) return;
				if (__instance == null || __instance.txtCongrats == null) return;
				string flawless = RDString.Get("status.allPurePerfect", null);
				if (string.IsNullOrEmpty(flawless)) return;
				string txt = __instance.txtCongrats.text;
				if (string.IsNullOrEmpty(txt) || !txt.Contains(flawless)) return;
				if (txt.StartsWith("<color=")) return; // 已添加过（防 coop/重复触发）

				double r = RainbowCounter.GetPerfectRatio();
				string xn;
				if (r <= 0.0001 || Math.Abs(RainbowState.AverageTimeMs) < 0.005) xn = "∞"; // 平均偏差显示0.00ms也视为∞（auto模式浮点误差特判）
				else
				{
					double n = (1.0 / r) - 1.0;
					xn = n < 1.0 ? n.ToString("F2") : FormatSig3(n);
				}
				int xIdx = RainbowCounter.GetXColorIndex();
				Color32 c;
				if (xIdx == 3) c = RainbowCounter.GetPerfectGreenColor(); // 2/3PP~PP档：原版完美绿
				else if (xIdx == 2) c = new Color32(0, 97, 121, 255); // 0.5PP~2/3PP档：计数蓝 480nm #006179
				else if (xIdx == 1) c = new Color32(0, 0, 103, 255); // 1/3PP~0.5PP档：计数青 440nm #000067
				else c = new Color32(57, 0, 85, 255); // 全1/3PP档：计数紫 400nm #390055
				string hex = Spectrum.ToHex(c);
				// 方案：文本流替换为"X 完美无瑕"（X用UI.Text渲染并染色），上标n由TMP渲染并放到空格右上角
				if (Main.Settings.DebugLog) Logger.Log("[FlawlessX] before=[" + txt.Replace("\n", "\\n") + "] flawless=[" + flawless + "] rich=" + __instance.txtCongrats.supportRichText + " fs=" + __instance.txtCongrats.fontSize + " font=" + (__instance.txtCongrats.font != null ? __instance.txtCongrats.font.name : "NULL"));
				__instance.txtCongrats.text = txt.Replace(flawless, "<color=#" + hex + ">X</color> " + flawless);
				if (Main.Settings.DebugLog) Logger.Log("[FlawlessX] after=[" + __instance.txtCongrats.text.Replace("\n", "\\n") + "]");
				FlawlessXOverlay.Show(__instance, xn, hex);
				if (Main.Settings.DebugLog)
					Logger.Log("[FlawlessX] 添加 X^" + xn + " (r=" + r.ToString("F4") + ", color=#" + hex + ")");
			}
			catch (Exception ex)
			{
				Logger.Log("[FlawlessX] " + ex.Message);
			}
		}

		

					/// <summary>n>=1 时三位有效数字（普通十进制；>=1000 才用科学计数 G3）</summary>
		private static string FormatSig3(double n)
		{
			if (n >= 1000.0) return n.ToString("G3");
			double abs = n;
			int f;
			if (abs >= 100.0) f = 0;
			else if (abs >= 10.0) f = 1;
			else f = 2;
			return n.ToString("F" + f);
		}

	}

	}

	/// <summary>叠加TMP上标：在 txtCongrats 下创建 TMP 子文本渲染 "X<sup>n</sup>"（TMP 支持真正的上标）。
	/// 定位：用 UI.Text 的 TextGenerator 取"完美无瑕"首字符顶点位置，TMP 右边缘对齐字符左侧。
	/// 生命周期：随 txtCongrats 销毁；新关卡/编辑器退出时主动 Hide。</summary>
	public static class FlawlessXOverlay
	{
		private static TextMeshProUGUI _overlay;
		private static string _xn = ""; // 当前上标内容（∞与一般数字用不同y偏移）

		public static void Show(scrController ctrl, string xn, string colorHex)
		{
			Hide();
			_xn = xn;
			try
			{
				if (ctrl == null || ctrl.txtCongrats == null) return;
				string full = ctrl.txtCongrats.text;
				if (string.IsNullOrEmpty(full)) return;
				GameObject go = new GameObject("RainbowXPower");
				go.transform.SetParent(ctrl.txtCongrats.transform, false);
				_overlay = go.AddComponent<TextMeshProUGUI>();
				_overlay.fontSize = 0.65f*ctrl.txtCongrats.fontSize;
				_overlay.alignment = TextAlignmentOptions.Left;
				_overlay.raycastTarget = false;
				TMP_FontAsset font = GetFont(ctrl.txtCongrats.font);
				if (font != null)
				{
					ApplyFont(font, xn, colorHex, ctrl);
				}
				else
				{
					if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] 字体获取失败，启动延迟重试");
					go.AddComponent<RetryRunner>().Init(ctrl, xn, colorHex);
				}
			}
			catch (Exception ex)
			{
				if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] " + ex.Message);
				Hide();
			}
		}

		private static void ApplyFont(TMP_FontAsset font, string xn, string colorHex, scrController ctrl)
		{
			try
			{
				if (_overlay == null) return;
				_overlay.font = font;
				_overlay.text = "<sup>" + xn + "</sup>";
				_overlay.color = ParseHex(colorHex);
				// 阴影深度/程度减半（实例化材质，不影响游戏共享字体材质）
				try
				{
					Material m = new Material(_overlay.fontSharedMaterial);
					m.SetFloat("_UnderlayOffsetX", 2f);   // 阴影向右（右下角）
					m.SetFloat("_UnderlayOffsetY", -2f);    // 阴影向下（右下角）
					m.SetFloat("_UnderlaySoftness", 0.2f); // 柔化小，边缘清晰
					Color uc = m.GetColor("_UnderlayColor"); uc.a *= 0.3f; m.SetColor("_UnderlayColor", uc);
					_overlay.fontSharedMaterial = m;
				}
				catch { }
				Position(ctrl);
				if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] 上标已显示: " + xn);
			}
			catch (Exception ex)
			{
				if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] ApplyFont: " + ex.Message);
				Hide();
			}
		}

		private class RetryRunner : MonoBehaviour
		{
			private scrController _ctrl;
			private string _xn;
			private string _colorHex;
			private float _elapsed;

			public void Init(scrController ctrl, string xn, string colorHex)
			{
				_ctrl = ctrl; _xn = xn; _colorHex = colorHex;
			}

			private void Update()
			{
				try
				{
					_elapsed += Time.deltaTime;
					if (_elapsed > 3f) { if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] 重试超时放弃"); Destroy(gameObject); return; }
					if (_ctrl == null || _ctrl.txtCongrats == null) { Destroy(gameObject); return; }
					TMP_FontAsset font = GetFont(_ctrl.txtCongrats.font);
					if (font != null)
					{
						ApplyFont(font, _xn, _colorHex, _ctrl);
						Destroy(gameObject);
					}
				}
				catch { Destroy(gameObject); }
			}
		}

		private static TMP_FontAsset GetFont(Font fallbackFont)
		{
			try
			{
				if (TMP_Settings.defaultFontAsset != null) { if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] font=TMP_Settings"); return TMP_Settings.defaultFontAsset; }
				TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
				if (all != null && all.Length > 0) { if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] font=场景字体"); return all[0]; }
				TextMeshProUGUI[] texts = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
				if (texts != null && texts.Length > 0 && texts[0].font != null) { if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] font=存活TMP组件"); return texts[0].font; }
				if (fallbackFont != null) // 兜底：从游戏UI字体动态创建TMP字体
				{
					TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(fallbackFont.name, "Normal", 90); // 新版TMP签名：(familyName, styleName, samplingPointSize)
					if (fa != null) { if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] font=动态创建"); return fa; }
				}
			}
			catch (Exception ex) { if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] GetFont异常: " + ex.Message); }
			return null;
		}

		private static Color32 ParseHex(string hex)
		{
			try
			{
				if (!string.IsNullOrEmpty(hex) && hex.Length == 6)
				{
					byte r = (byte)System.Convert.ToInt32(hex.Substring(0, 2), 16);
					byte g = (byte)System.Convert.ToInt32(hex.Substring(2, 2), 16);
					byte b = (byte)System.Convert.ToInt32(hex.Substring(4, 2), 16);
					return new Color32(r, g, b, 255);
				}
			}
			catch { }
			return new Color32(95, 255, 78, 255);
		}

		private static void Position(scrController ctrl)
		{
			try
			{
				var text = ctrl.txtCongrats;
				float fs = text.fontSize;
				string flawless = RDString.Get("status.allPurePerfect", null);
				if (string.IsNullOrEmpty(flawless)) flawless = "完美无瑕！";
				// 估算文本总宽：X(0.6fs)+空格(0.3fs)+flawless（CJK字符=1fs，其他=0.6fs）
				float w = 0.9f * fs;
				foreach (char ch in flawless) w += (ch >= 0x2E80 ? 1.0f : 0.6f) * fs;
				// 居中文本：空格中心 = -总宽/2 + X宽 + 空格半宽
				// 实测修正：x右移1字符再往回20px(0.1fs)；y向上为正方向(+0.6fs，字符顶端)
				float xRel = -w * 0.5f + 1.55f * fs;
				float yRel = (_xn == "∞") ? 0.35f * fs : 0.25f * fs; // ∞:0.35fs，一般数字:0.25fs
				RectTransform rt = (RectTransform)_overlay.transform;
				rt.pivot = new Vector2(0.5f, 1f); // 顶中：上标数字中心落在空格右上角
				rt.anchorMin = new Vector2(0.5f, 0.5f);
				rt.anchorMax = new Vector2(0.5f, 0.5f);
				rt.anchoredPosition = new Vector2(xRel, yRel);
				if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] pos=(" + xRel.ToString("F1") + "," + yRel.ToString("F1") + ") estW=" + w.ToString("F1") + " fs=" + fs + " flawless=" + flawless + " align=" + text.alignment + " anchor=" + text.rectTransform.anchorMin + " pivot=" + text.rectTransform.pivot + " anchored=" + text.rectTransform.anchoredPosition + " rect=" + text.rectTransform.rect);
			}
			catch (Exception ex) { if (Main.Settings.DebugLog) Logger.Log("[FlawlessXOverlay] Position: " + ex.Message); }
		}

		public static void Hide()
		{
			if (_overlay != null)
			{
				UnityEngine.Object.Destroy(_overlay.gameObject);
				_overlay = null;
			}
		}
	}
}
