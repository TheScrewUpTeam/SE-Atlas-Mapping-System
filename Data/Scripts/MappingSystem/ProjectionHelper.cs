using System;
using VRageMath;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities;

namespace TSUT.MappingSystem
{
    public static class ProjectionHelper
    {
        public static Vector2I WorldToGrid(Vector3D worldPos, int cellSize)
        {
            if (Config.Instance.AlignToGravity)
            {
                MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(worldPos);
                if (planet != null)
                {
                    Vector3D localDir = Vector3D.Normalize(worldPos - planet.PositionComp.GetPosition());
                    
                    // Equirectangular projection
                    // Lon: [-PI, PI], Lat: [-PI/2, PI/2]
                    double lon = Math.Atan2(localDir.X, localDir.Z);
                    double lat = Math.Asin(MathHelper.Clamp(localDir.Y, -1, 1));

                    double radius = planet.AverageRadius;
                    return new Vector2I(
                        (int)Math.Floor((lon * radius) / cellSize),
                        (int)Math.Floor((lat * radius) / cellSize)
                    );
                }
            }

            // Fallback to World X/Z
            return new Vector2I(
                (int)Math.Floor(worldPos.X / cellSize),
                (int)Math.Floor(worldPos.Z / cellSize)
            );
        }

        public static Vector3D GridToWorld(Vector2I gridPos, int cellSize, float elevation, MyPlanet planet = null)
        {
            if (Config.Instance.AlignToGravity)
            {
                if (planet != null)
                {
                    double radius = planet.AverageRadius;
                    double lon = (gridPos.X * cellSize + cellSize * 0.5) / radius;
                    double lat = (gridPos.Y * cellSize + cellSize * 0.5) / radius;

                    double cosLat = Math.Cos(lat);
                    Vector3D localDir = new Vector3D(
                        Math.Sin(lon) * cosLat,
                        Math.Sin(lat),
                        Math.Cos(lon) * cosLat
                    );

                    return planet.PositionComp.GetPosition() + localDir * (radius + elevation);
                }
            }

            // Fallback to World X/Z (Y=Elevation + PlanetAvg if on planet?)
            // Actually, storage Height is relative to Planet Average.
            // If no planet, it's just absolute Y.
            double y = elevation;
            if (planet != null) y += planet.AverageRadius;
            
            return new Vector3D(gridPos.X * cellSize + cellSize * 0.5, y, gridPos.Y * cellSize + cellSize * 0.5);
        }

        public static Vector3D GetLocalUp(Vector3D worldPos)
        {
            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(worldPos);
            if (planet != null)
            {
                return Vector3D.Normalize(worldPos - planet.PositionComp.GetPosition());
            }
            return Vector3D.Up;
        }

        public static MatrixD GetProjectionMatrix(Vector3D worldPos)
        {
            Vector3D up = GetLocalUp(worldPos);
            
            // Tangent plane axes must match WorldToGrid:
            // X (Right) = West -> East (Longitude change)
            // Y (Forward) = South -> North (Latitude change)
            
            // Geographic North in World Space is +Y (0, 1, 0)
            Vector3D north = Vector3D.Up;
            Vector3D forward = Vector3D.Normalize(Vector3D.Reject(north, up));
            
            // If at poles, fallback to standard orientation
            if (forward.LengthSquared() < 0.001)
            {
                forward = Vector3D.Forward;
            }

            return MatrixD.CreateWorld(worldPos, forward, up);
        }
    }
}
