using System;
using UnityEngine;

namespace RainbowJudgement
{
	public static class RainbowMath
	{
		public const float CountedScaled = 60f;
		public static readonly Color32 RedColor = new Color32(255, 0, 0, 255);
		public const double MinWavelengthNm = 380.0;
		public const double RedWavelengthNm = 700.0;

		/// <summary>当前速度：与游戏 GetAdjustedAngleBoundaryInDeg 一致（currentSpeedTrial 优先，回退 prevfloor.speed）</summary>
		public static double GetCurrentSpeed()
		{
			try
			{
				double speed = GCS.currentSpeedTrial;
				if (speed <= 0.0001)
				{
					scrController ctrl = scrController.instance;
					if (ctrl != null && ctrl.playerOne != null && ctrl.playerOne.currFloor != null && ctrl.playerOne.currFloor.prevfloor != null)
						speed = ctrl.playerOne.currFloor.prevfloor.speed;
					if (speed <= 0.0001) speed = 1.0;
				}
				return speed;
			}
			catch { return 1.0; }
		}

		/// <summary>Counted 时间下限(ms)：随难度 40/65/91ms，除以当前速度，绝对下限 25ms（与游戏一致）</summary>
		public static double CountedTimeMs()
		{
			try
			{
				double t = 0.065;
				int d = (int)GCS.difficulty;
				if (d == 0) t = 0.091;
				else if (d == 2) t = 0.04;
				double speed = GetCurrentSpeed();
				return Math.Max(t / speed, 0.025) * 1000.0;
			}
			catch { return 65.0; }
		}

		/// <summary>复刻原版 AddHit 的 speed 取值：hitFloor.speed → playerOne.prevfloor.speed → 1</summary>
		public static float GameSpeedOf(scrFloor hitFloor)
		{
			try
			{
				if (hitFloor != null) return hitFloor.speed;
				scrController ctrl = scrController.instance;
				if (ctrl != null && ctrl.playerOne != null && ctrl.playerOne.currFloor != null && ctrl.playerOne.currFloor.prevfloor != null)
					return ctrl.playerOne.currFloor.prevfloor.speed;
				return 1f;
			}
			catch { return 1f; }
		}

		/// <summary>判定/刻度同款 bpm×speed（hitFloor 基准，与原版 AddHit/CalculateTickColor 一致）</summary>
		public static double GameBpmTimesSpeedOf(scrFloor hitFloor)
		{
			try { return scrConductor.instance.bpm * GameSpeedOf(hitFloor); }
			catch { return 120.0; }
		}

		public static double GetPitchNow()
		{
			try { return scrConductor.instance.song.pitch; }
			catch { return 1.0; }
		}

		/// <summary>缩放角度 → 真实误差时间(ms)：缩放/60×CountedDeg→真实角度→时间（角速度系数3，基准=判定瞬间，含 marginScale）</summary>
		public static double TimeMsFromScaledAngleAccurate(float scaledAngle, double countedDeg, double bpmTimesSpeed, double conductorPitch)
		{
			double angleSpeed = bpmTimesSpeed * conductorPitch * 3.0; // 1拍=180°，与 TimeToAngleInRad 系数一致
			if (double.IsNaN(angleSpeed) || angleSpeed <= 0.0001) return 0.0;
			if (double.IsNaN(countedDeg) || countedDeg <= 0.0001) return 0.0;
			double realAngle = scaledAngle / (double)CountedScaled * countedDeg;
			if (double.IsNaN(realAngle)) return 0.0;
			return realAngle / angleSpeed * 1000.0;
		}

		/// <summary>档位边界角度(度)：max(角度下限×mult, 时间下限对应角度)。与原版 PureDeg/PerfectDeg 同构，用游戏自身的方法算。</summary>
		public static double LevelBoundaryDeg(int levelIndex, double bpmTimesSpeed, double conductorPitch, double marginScale)
		{
			double timeSec, angleDeg;
			switch (levelIndex)
			{
				case 0: timeSec = 0.0075;  angleDeg = 10.0; break; // 1/3PP
				case 1: timeSec = 0.0125;  angleDeg = 15.0; break; // 0.5PP
				case 2: timeSec = 0.01667; angleDeg = 20.0; break; // 2/3PP
				case 3: { double trial3 = GCS.currentSpeedTrial; if (trial3 <= 0.0001) trial3 = 1.0; timeSec = Math.Max(0.02 / trial3, 0.025); angleDeg = 30.0; break; } // PP（=原版 PureDeg：max(0.02/currentSpeedTrial,0.025)）
				case 4: { double trial4 = GCS.currentSpeedTrial; if (trial4 <= 0.0001) trial4 = 1.0; timeSec = Math.Max(0.03 / trial4, 0.025); angleDeg = 45.0; break; } // LP/EP（=原版 PerfectDeg：max(0.03/currentSpeedTrial,0.025)）
				default: // 5: 太快太慢 = 原版 CountedDeg
					return scrMisc.GetAdjustedAngleBoundaryInDeg(HitMarginGeneral.Counted, bpmTimesSpeed, conductorPitch, marginScale);
			}
			double timeAngle = scrMisc.TimeToAngleInRad(timeSec, bpmTimesSpeed, conductorPitch, false) * 57.29578;
			return Math.Max(angleDeg * marginScale, timeAngle);
		}

		/// <summary>锚点固定波长(nm)：极低 bpm 下各档位边界在仪表盘(线性 380~700)上的对应色 = 380+320×(角度下限/60)</summary>
		private static readonly double[] ANCHOR_WAVELENGTH = { 380.0 + 320.0 * 10.0 / 60.0, 380.0 + 320.0 * 15.0 / 60.0, 380.0 + 320.0 * 20.0 / 60.0, 380.0 + 320.0 * 30.0 / 60.0, 380.0 + 320.0 * 45.0 / 60.0, 700.0 };

		/// <summary>档位锚点位置(缩放坐标)：边界角度×60/countedDeg（与原版分档边界缩放同构）</summary>
		public static double LevelAnchorPosition(int levelIndex, double countedDeg, double bpmTimesSpeed, double conductorPitch, double marginScale)
		{
			if (double.IsNaN(countedDeg) || countedDeg <= 0.0001) return 0.0;
			double boundaryDeg = LevelBoundaryDeg(levelIndex, bpmTimesSpeed, conductorPitch, marginScale);
			if (double.IsNaN(boundaryDeg)) return 0.0;
			return boundaryDeg * CountedScaled / countedDeg;
		}

		/// <summary>档位纯色(0..5)：433.3/460/486.7/540/620/700nm（供文字/分档使用）</summary>
		public static Color32 TierColor(int tier)
		{
			if (tier < 0 || tier > 5) return Spectrum.WavelengthToRgb(700.0);
			return Spectrum.WavelengthToRgb(ANCHOR_WAVELENGTH[tier]);
		}

		/// <summary>锚点间线性渐变：pos=缩放刻度；锚点位置=LevelBoundaryDeg×60/countedDeg（与原版判定边界同构）；锚点色=ANCHOR_WAVELENGTH固定</summary>
		public static double WavelengthForGradient(double pos, double countedDeg, double bpmTimesSpeed, double conductorPitch, double marginScale)
		{
			double[] anchors = new double[6];
			for (int i = 0; i < 6; i++) anchors[i] = LevelAnchorPosition(i, countedDeg, bpmTimesSpeed, conductorPitch, marginScale);
			double pMax = anchors[5];
			if (pMax <= 0.0001) pMax = CountedScaled;
			if (pos >= pMax) return RedWavelengthNm;
			double w0 = MinWavelengthNm;
			for (int i = 0; i < 6; i++)
			{
				double w1 = ANCHOR_WAVELENGTH[i];
				double a0 = i == 0 ? 0.0 : anchors[i - 1];
				if (pos <= anchors[i] || i == 5)
				{
					double span = anchors[i] - a0;
					double tt = span <= 0.0001 ? 1.0 : Math.Min((pos - a0) / span, 1.0);
					return w0 + (w1 - w0) * tt;
				}
				w0 = w1;
			}
			return RedWavelengthNm;
		}
	}
}
