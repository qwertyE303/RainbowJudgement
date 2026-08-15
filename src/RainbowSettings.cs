using UnityModManagerNet;

namespace RainbowJudgement
{
    public class RainbowSettings : UnityModManager.ModSettings
    {
        /// <summary>Level 1: master switch (rainbow judgment mode)</summary>
        public bool EnableRainbow = true;
        /// <summary>Level 2: show average judgment info on results screen</summary>
        public bool ShowAverageJudgment = true;
        /// <summary>Level 3: show average judgment time</summary>
        public bool ShowAverageTime = true;
        /// <summary>Level 3: show average judgment color</summary>
        public bool ShowAverageColor = true;
        /// <summary>Debug log (for development)</summary>
        public bool DebugLog = false;
		public bool ShowRainbowCounter = false;
		public int CounterFontSize = 44;
		public int CounterX = 0;
		public int CounterY = 220;
		public int CounterSpacing = 1;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}

