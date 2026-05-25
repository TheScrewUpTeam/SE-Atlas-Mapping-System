using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace TSUT.MappingSystem
{
    public class ScanVisual
    {
        public Vector3D Position;
        public float MaxRadius;
        public int DurationTicks;
        public int ElapsedTicks;
        public int FadeTicks = 30; // 0.5s
        public long AntennaId;

        public ScanVisual(Vector3D position, float radius, int durationTicks, long antennaId = 0)
        {
            Position = position;
            MaxRadius = radius;
            DurationTicks = durationTicks;
            ElapsedTicks = 0;
            AntennaId = antennaId;
        }
    }

    public partial class MapSession
    {
        private readonly List<ScanVisual> _activeVisuals = new List<ScanVisual>();
        private readonly MyStringId _material = MyStringId.GetOrCompute("SafeZoneShield_Material");

        public void AddScanVisual(Vector3D position, float radius, int durationTicks, long antennaId = 0)
        {
            _activeVisuals.Add(new ScanVisual(position, radius, durationTicks, antennaId));
            
            if (antennaId != 0)
            {
                var entity = MyAPIGateway.Entities.GetEntityById(antennaId);
                var entry = entity?.GameLogic?.GetAs<ScannerEntry>();
                if (entry != null)
                {
                    entry.IsScanning = true;
                    (entity as IMyTerminalBlock)?.RefreshCustomInfo();
                }
            }
        }

        private void UpdateVisuals()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;
            if (_activeVisuals.Count == 0) return;

            for (int i = _activeVisuals.Count - 1; i >= 0; i--)
            {
                var visual = _activeVisuals[i];
                visual.ElapsedTicks++;

                if (visual.AntennaId != 0)
                {
                    var entity = MyAPIGateway.Entities.GetEntityById(visual.AntennaId);
                    if (entity != null)
                    {
                        visual.Position = entity.WorldMatrix.Translation;
                    }
                }

                float progress = (float)visual.ElapsedTicks / visual.DurationTicks;
                float currentRadius;
                float alpha;

                if (progress <= 1.0f)
                {
                    // Growth phase: 0 to MaxRadius
                    currentRadius = visual.MaxRadius * progress;
                    alpha = .75f; 
                }
                else
                {
                    // Fade phase: Fixed MaxRadius, decreasing alpha
                    currentRadius = visual.MaxRadius;
                    float fadeProgress = (float)(visual.ElapsedTicks - visual.DurationTicks) / visual.FadeTicks;
                    alpha = .75f * MathHelper.Clamp(1.0f - fadeProgress, 0f, 1f);
                }


                if (alpha > 0)
                {
                    var color = new Color(100, 200, 255, (int)(255 * alpha));
                    var matrix = MatrixD.CreateTranslation(visual.Position);
                    MySimpleObjectDraw.DrawTransparentSphere(ref matrix, currentRadius, ref color, MySimpleObjectRasterizer.Solid, 20, _material, _material, 0.05f);
                }

                if (visual.ElapsedTicks >= visual.DurationTicks + visual.FadeTicks)
                {
                    if (visual.AntennaId != 0)
                    {
                        var entity = MyAPIGateway.Entities.GetEntityById(visual.AntennaId);
                        var entry = entity?.GameLogic?.GetAs<ScannerEntry>();
                        if (entry != null)
                        {
                            entry.IsScanning = false;
                            (entity as IMyTerminalBlock)?.RefreshCustomInfo();
                        }
                    }
                    _activeVisuals.RemoveAt(i);
                }
            }
        }
    }
}
