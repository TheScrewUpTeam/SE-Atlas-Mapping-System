using Sandbox.ModAPI;
using VRage.Game;

namespace TSUT.MappingSystem
{
    public static class AntennaHelper
    {
        public static float GetScanRadius(IMyRadioAntenna antenna)
        {
            float factor = antenna.CubeGrid.GridSizeEnum == MyCubeSize.Large ? Config.Instance.ScanRadiusFactorLarge : Config.Instance.ScanRadiusFactorSmall;
            return antenna.Radius * factor;
        }

        public static float GetScanPowerMultiplier(IMyRadioAntenna antenna)
        {
            return antenna.CubeGrid.GridSizeEnum == MyCubeSize.Large ? Config.Instance.ScanPowerMultiplierLarge : Config.Instance.ScanPowerMultiplierSmall;
        }
    }
}
