using System;
using Sandbox.ModAPI;
using VRage.Utils;
using System.Xml.Serialization;

namespace TSUT.MappingSystem
{
    public class Config
    {
        public static readonly string CurrentVersion = "1.0.2";
        public static readonly ushort NetworkId = 54321;
        public static readonly string ConfigFileName = "MappingSystem_Config.xml";
        public static readonly Guid StorageGuid = new Guid("8E373F26-3C72-4742-87C2-79013A63A294");
        public static readonly string LogPrefix = "[AtlasMappingSystem]";

        public string ModVersion { get; set; } = CurrentVersion;
        public bool AutoUpdateConfig { get; set; } = true;

        public int CellSize { get; set; } = 100;
        public bool AlignToGravity { get; set; } = true;
        public float ScanRadiusFactorSmall { get; set; } = 0.02f;
        public float ScanPowerMultiplierSmall { get; set; } = 1000.0f;
        public float ScanRadiusFactorLarge { get; set; } = 0.05f;
        public float ScanPowerMultiplierLarge { get; set; } = 5000.0f;
        public int MaxRaycastsPerTick { get; set; } = 50;
        public int MaxScanPunchThroughs { get; set; } = 5;
        public int ScanOversampleFactor { get; set; } = 3;
        public float PunchThroughOffset { get; set; } = 0.5f;
        public float MarkerUpdateIntervalSeconds { get; set; } = 10f;

        public int MaxChunksSmallGrid { get; set; } = 20;
        public int MaxChunksLargeGrid { get; set; } = 200;
        public int MaxChunksStaticGrid { get; set; } = 500000;

        // ── Contract tuning ──────────────────────────────────────────────────────
        public float[] ContractRadiiMeters { get; set; } = { 1000f, 2000f, 3000f };

        // Shared reward/rep/duration multipliers (per-km² of radius, or per km of radius)
        public double ContractRewardPerRadiusKm2 { get; set; } = 120000.0;
        public double ContractRepPerRadiusKm { get; set; } = 100.0;
        public double ContractDurationPerRadiusKm2 { get; set; } = 5.0;

        // Remote survey additional multipliers
        public double RemoteRewardPerDistanceKm { get; set; } = 15000.0;
        public double RemoteFailRepPerRadiusKm { get; set; } = 50.0;
        public double RemoteDurationPerDistanceKm { get; set; } = 0.5;
        public double RemoteMinDistanceMeters { get; set; } = 10000.0;
        public double RemoteMaxDistanceMeters { get; set; } = 50000.0;

        // Dangerous survey multipliers (applied on top of shared values)
        public double ContractDangerousRewardMultiplier { get; set; } = 1.5;
        public double ContractDangerousCollateralFraction { get; set; } = 0.2;
        public double ContractDangerousDurationMultiplier { get; set; } = 2.0;
        public double ContractDangerousMinDistanceMeters { get; set; } = 10000.0;

        private static Config _instance;
        public static Config Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        public static Config Load()
        {
            Config config = null;
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(ConfigFileName, typeof(Config)))
                {
                    string xml;
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(ConfigFileName, typeof(Config)))
                    {
                        xml = reader.ReadToEnd();
                    }

                    if (string.IsNullOrEmpty(xml))
                    {
                        MyLog.Default.WriteLine($"{Config.LogPrefix} Config file is empty.");
                    }
                    else
                    {
                        config = MyAPIGateway.Utilities.SerializeFromXML<Config>(xml);
                        
                        if (config != null && config.AutoUpdateConfig && config.ModVersion != CurrentVersion)
                        {
                            MyLog.Default.WriteLine($"{Config.LogPrefix} Config version mismatch ({config.ModVersion} vs {CurrentVersion}). Resetting to defaults.");
                            config = new Config();
                            Save(config);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error loading config: {ex.Message}");
            }

            if (config == null)
            {
                config = new Config();
                Save(config);
            }

            return config;
        }

        public static void Save(Config config)
        {
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(ConfigFileName, typeof(Config)))
                {
                    string xml = MyAPIGateway.Utilities.SerializeToXML(config);
                    writer.Write(xml);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error saving config: {ex.Message}");
            }
        }
    }
}
