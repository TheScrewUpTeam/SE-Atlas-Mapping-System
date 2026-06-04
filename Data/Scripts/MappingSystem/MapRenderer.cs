using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using Sandbox.Game.Entities;

namespace TSUT.MappingSystem
{
    public class MapRenderer
    {
        public struct MapMarker
        {
            public Vector3D WorldPosition;
            public string Label;
            public Color Color;
        }

        private readonly Sandbox.ModAPI.Ingame.IMyTextSurface _surface;
        private MapGrid _grid;
        private Vector2 _pan;
        private float _zoom;
        private long _hostAntennaId;
        private List<MapMarker> _markers = new List<MapMarker>();

        public MapRenderer(Sandbox.ModAPI.Ingame.IMyTextSurface surface, MapGrid grid, long hostAntennaId)
        {
            _surface = surface;
            _grid = grid;
            _hostAntennaId = hostAntennaId;
            _zoom = 20f;
            _pan = Vector2.Zero;
        }

        public void UpdateMarkers(List<MapMarker> markers) { _markers = markers; }
        public void UpdateGrid(MapGrid grid) { _grid = grid; }

        public void SetView(Vector2 pan, float zoom)
        {
            _pan = pan;
            _zoom = MathHelper.Clamp(zoom, 1f, 500f);
        }

        public void Draw(MatrixD antennaMatrix, bool isStatic, float heading, bool rotateMap, bool isScanning, string eta,
            List<ClientContractInfo> contracts = null)
        {
            if (_surface == null) return;

            Vector2 surfaceSize = _surface.SurfaceSize;
            Vector2 textureSize = _surface.TextureSize;
            Vector2 viewportOffset = (textureSize - surfaceSize) / 2f;
            Vector2 mapCenter = viewportOffset + (surfaceSize / 2f);

            RenderContext context = PrepareContext(antennaMatrix, isStatic, heading, rotateMap, mapCenter, viewportOffset, surfaceSize);

            using (MySpriteDrawFrame frame = _surface.DrawFrame())
            {
                DrawBackground(frame, context);
                DrawTerrain(frame, context, contracts);
                if (contracts != null && contracts.Count > 0)
                    DrawContractZones(frame, context, contracts);
                DrawMarkers(frame, context);
                DrawUserIndicator(frame, context, antennaMatrix.Translation);
                DrawStatus(frame, context, isScanning, eta);
            }
        }

        private struct RenderContext
        {
            public Vector3D ViewCenter;
            public MatrixD ProjectionMatrix;
            public MyPlanet Planet;
            public float Cos;
            public float Sin;
            public bool IsStatic;
            public bool RotateMap;
            public Vector2 MapCenter;
            public Vector2 ViewportOffset;
            public Vector2 SurfaceSize;
            public float Zoom;
            public float Heading;
        }

        private RenderContext PrepareContext(MatrixD antennaMatrix, bool isStatic, float heading, bool rotateMap, Vector2 mapCenter, Vector2 viewportOffset, Vector2 surfaceSize)
        {
            Vector3D antennaPos = antennaMatrix.Translation;
            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(antennaPos);
            Vector3D viewCenter;
            MatrixD projectionMatrix;

            if (Config.Instance.AlignToGravity && planet != null)
            {
                projectionMatrix = ProjectionHelper.GetProjectionMatrix(antennaPos);
                viewCenter = antennaPos + (projectionMatrix.Right * _pan.X) + (projectionMatrix.Forward * _pan.Y);
                projectionMatrix = ProjectionHelper.GetProjectionMatrix(viewCenter);
            }
            else
            {
                viewCenter = antennaPos + new Vector3D(_pan.X, 0, _pan.Y);
                projectionMatrix = MatrixD.Identity;
            }

            return new RenderContext
            {
                ViewCenter = viewCenter,
                ProjectionMatrix = projectionMatrix,
                Planet = planet,
                Cos = (float)Math.Cos(heading),
                Sin = (float)Math.Sin(heading),
                IsStatic = isStatic,
                RotateMap = rotateMap,
                MapCenter = mapCenter,
                ViewportOffset = viewportOffset,
                SurfaceSize = surfaceSize,
                Zoom = _zoom,
                Heading = heading
            };
        }

        private void DrawBackground(MySpriteDrawFrame frame, RenderContext context)
        {
            var bg = new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Color = Color.Black,
                Position = new Vector2(0, context.MapCenter.Y),
                Size = context.SurfaceSize * 2
            };
            frame.Add(bg);
        }

        private void DrawTerrain(MySpriteDrawFrame frame, RenderContext context, List<ClientContractInfo> contracts)
        {
            int cellSize = Config.Instance.CellSize;
            bool hasContracts = contracts != null && contracts.Count > 0;
            long oldestAcceptedTicks = hasContracts ? long.MaxValue : 0;
            if (hasContracts)
                foreach (var c in contracts)
                    if (c.AcceptedTicks < oldestAcceptedTicks) oldestAcceptedTicks = c.AcceptedTicks;

            foreach (var chunk in _grid.Chunks.Values)
            {
                bool chunkUsed = false;
                bool chunkFresh = hasContracts && chunk.LastWrittenTicks >= oldestAcceptedTicks;

                foreach (var kvp in chunk.Cells)
                {
                    Vector3D worldPos = ProjectionHelper.GridToWorld(kvp.Key, cellSize, kvp.Value.Height, context.Planet);
                    Vector2 screenPos = WorldToScreen(worldPos, context);

                    if (IsInViewport(screenPos, context))
                    {
                        var color = GetHeightColor(kvp.Value);

                        if (hasContracts)
                        {
                            bool inZone = false;
                            foreach (var contract in contracts)
                            {
                                if (contract.CoverageMet) continue;
                                if (Vector3D.DistanceSquared(worldPos, contract.Center) <= (double)contract.Radius * contract.Radius)
                                { inZone = true; break; }
                            }
                            if (inZone && !chunkFresh)
                                color = Color.Lerp(color, Color.DarkGray, 0.55f);
                        }

                        float pixelSize = Math.Max(1f, cellSize / context.Zoom);
                        frame.Add(new MySprite()
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Color = color,
                            Position = screenPos,
                            Size = new Vector2(pixelSize, pixelSize),
                            RotationOrScale = -context.Heading
                        });
                        chunkUsed = true;
                    }
                }

                if (chunkUsed) chunk.Touch();
            }
        }

        private void DrawContractZones(MySpriteDrawFrame frame, RenderContext context, List<ClientContractInfo> contracts)
        {
            int cellSize = Config.Instance.CellSize;
            foreach (var contract in contracts)
            {
                if (contract.CoverageMet) continue;
                // Round-trip through equirectangular grid so the center lies on the sphere surface,
                // matching how terrain cells are positioned (GridToWorld → WorldToScreen).
                Vector3D mappedCenter = contract.Center;
                if (Config.Instance.AlignToGravity && context.Planet != null)
                {
                    var gridPos = ProjectionHelper.WorldToGrid(contract.Center, cellSize);
                    mappedCenter = ProjectionHelper.GridToWorld(gridPos, cellSize, 0, context.Planet);
                }

                Vector2 screenCenter = WorldToScreen(mappedCenter, context);
                float screenRadius = contract.Radius / context.Zoom;
                if (screenRadius < 1f) continue;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE, Data = "CircleHollow",
                    Color = Color.Red,
                    Position = new Vector2(screenCenter.X - screenRadius, screenCenter.Y),
                    Size = new Vector2(screenRadius * 2f, screenRadius * 2f)
                });
            }
        }

        private void DrawMarkers(MySpriteDrawFrame frame, RenderContext context)
        {
            foreach (var marker in _markers)
            {
                Vector2 screenPos = WorldToScreen(marker.WorldPosition, context);
                if (IsInViewport(screenPos, context))
                {
                    DrawMarker(frame, screenPos, marker.Color);
                }
            }
        }

        private void DrawUserIndicator(MySpriteDrawFrame frame, RenderContext context, Vector3D antennaPos)
        {
            float markerRotation;
            if (!context.IsStatic && context.RotateMap)
            {
                markerRotation = 0f;
            }
            else
            {
                markerRotation = context.IsStatic ? (float)Math.PI : context.Heading;
            }

            Vector2 indScreenPos = WorldToScreen(antennaPos, context);
            DrawIndicator(frame, indScreenPos, context.IsStatic, markerRotation);
        }

        private void DrawStatus(MySpriteDrawFrame frame, RenderContext context, bool isScanning, string eta)
        {
            if (isScanning)
            {
                string statusText = "Scanning...";
                var textSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = statusText + " ETA: " + eta,
                    Position = context.ViewportOffset + new Vector2(10, 10),
                    Color = Color.White,
                    FontId = "Monospace",
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = 0.6f
                };
                frame.Add(textSprite);
            }
        }

        private Vector2 WorldToScreen(Vector3D worldPos, RenderContext context)
        {
            Vector3D relativePos = worldPos - context.ViewCenter;
            float localX, localY;

            if (Config.Instance.AlignToGravity && context.Planet != null)
            {
                localX = (float)Vector3D.Dot(relativePos, context.ProjectionMatrix.Right);
                localY = (float)Vector3D.Dot(relativePos, context.ProjectionMatrix.Forward);
            }
            else
            {
                localX = (float)relativePos.X;
                localY = (float)-relativePos.Z;
            }

            Vector2 screenOffset;
            if (!context.IsStatic && context.RotateMap)
            {
                float rotX = localX * context.Cos - localY * context.Sin;
                float rotY = localX * context.Sin + localY * context.Cos;
                screenOffset = new Vector2(rotX, -rotY);
            }
            else
            {
                screenOffset = new Vector2(localX, -localY);
            }

            return screenOffset / context.Zoom + context.MapCenter;
        }

        private bool IsInViewport(Vector2 screenPos, RenderContext context)
        {
            return screenPos.X >= context.ViewportOffset.X && screenPos.X <= context.ViewportOffset.X + context.SurfaceSize.X &&
                   screenPos.Y >= context.ViewportOffset.Y && screenPos.Y <= context.ViewportOffset.Y + context.SurfaceSize.Y;
        }

        public void DrawNoData()
        {
            if (_surface == null) return;

            using (MySpriteDrawFrame frame = _surface.DrawFrame())
            {
                Vector2 surfaceSize = _surface.SurfaceSize;
                Vector2 textureSize = _surface.TextureSize;
                Vector2 viewportOffset = (textureSize - surfaceSize) / 2f;
                Vector2 mapCenter = viewportOffset + (surfaceSize / 2f);

                var bg = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Color = Color.Black,
                    Position = new Vector2(0, mapCenter.Y),
                    Size = surfaceSize * 2
                };
                frame.Add(bg);

                var textSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = "NO MAP DATA",
                    Position = mapCenter + new Vector2(0, -20), // Slight offset for alignment
                    Color = Color.Red,
                    FontId = "Monospace",
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = 1.0f
                };
                frame.Add(textSprite);
            }
        }

        private void DrawMarker(MySpriteDrawFrame frame, Vector2 position, Color color)
        {
            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Color = Color.Black,
                Position = position - new Vector2(7f, 0) / 2,
                Size = new Vector2(7f, 7f)
            });

            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Color = color,
                Position = position - new Vector2(5f, 0) / 2,
                Size = new Vector2(5f, 5f)
            });
        }

        private void DrawIndicator(MySpriteDrawFrame frame, Vector2 position, bool isStatic, float rotation)
        {
            Color markerColor = isStatic ? Color.White : Color.Red;

            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Color = Color.Black,
                Position = position - new Vector2(10f, 0) / 2,
                Size = new Vector2(10f, 10f)
            });

            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Color = markerColor,
                Position = position - new Vector2(7f, 0) / 2,
                Size = new Vector2(7f, 7f)
            });

            if (!isStatic)
            {
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Color = markerColor,
                    Position = position + new Vector2(-1f, -14f),
                    Size = new Vector2(2f, 20f),
                    RotationOrScale = rotation
                });
            }
        }


        private Color GetHeightColor(MapCell cell)
        {
            if (cell.Flags == 2) return Color.Gray; // Asteroid

            float avg = _grid.AverageHeight;
            float h = cell.Height - avg; // Height relative to average terrain

            // Real-world-ish topographic convention
            if (h < -50) return Color.Lerp(Color.Navy, Color.Blue, MathHelper.Clamp((h + 500) / 450f, 0, 1));
            if (h < 0) return Color.Lerp(Color.Blue, Color.Cyan, (h + 50) / 50f);
            if (h < 20) return Color.Lerp(Color.SandyBrown, Color.DarkGreen, h / 20f);
            if (h < 200) return Color.Lerp(Color.DarkGreen, Color.Green, (h - 20) / 180f);
            if (h < 600) return Color.Lerp(Color.Green, Color.Olive, (h - 200) / 400f);
            if (h < 1200) return Color.Lerp(Color.Olive, Color.SaddleBrown, (h - 600) / 600f);
            if (h < 2000) return Color.Lerp(Color.SaddleBrown, Color.DimGray, (h - 1200) / 800f);
            return Color.Lerp(Color.DimGray, Color.White, MathHelper.Clamp((h - 2000) / 1000f, 0, 1));
        }
    }
}
