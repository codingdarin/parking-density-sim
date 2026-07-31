using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ParkingSim.Core.V2;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ParkingSim.Runtime
{
    partial class SimulationRunner
    {
        private void BuildPresentationGround()
        {
            EnsureSiteMaterials();
            if (_siteRoadMesh != null)
            {
                Destroy(_siteRoadMesh);
                _siteRoadMesh = null;
            }
            GameObject siteBase = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Track(siteBase, SimulationVisualLayer.ThreeDimensional);
            siteBase.name = "ApartmentComplex-LandscapeBase";
            siteBase.transform.position = new Vector3(
                (_problem.Width - 1) * 0.5f,
                RoadSurfaceHeight - 0.04f,
                (_problem.Height - 1) * 0.5f);
            siteBase.transform.localScale = new Vector3(
                (_problem.Width + 4f) / 10f,
                1f,
                (_problem.Height + 4f) / 10f);
            SetSharedMaterial(siteBase, _siteGrassMaterial);
            DisableColliders(siteBase);

            var roadCells = new List<(int X, int Y)>();
            for (int y = 0; y < _problem.Height; y++)
                for (int x = 0; x < _problem.Width; x++)
                    if (_problem.IsFloor(x, y))
                        roadCells.Add((x, y));
            BuildCellSurfaceMesh(
                "ApartmentComplex-RoadSurface",
                roadCells,
                RoadSurfaceHeight,
                _siteAsphaltMaterial,
                0.08f);
            BuildSiteBoundary();
            BuildParkingSpaceMarkings();
            BuildRoadLaneMarkings();
            BuildFireEngineZoneMarkings();
            BuildLandscapeProps();
        }

        private void EnsureSiteMaterials()
        {
            if (_siteAsphaltMaterial == null)
            {
                _siteAsphaltMaterial =
                    SimulationVisualAssetFactory.TryCreateAsphaltMaterial();
                if (_siteAsphaltMaterial == null)
                {
                    _siteAsphaltMaterial = CreateSiteMaterial(
                        "Fallback-Asphalt",
                        new Color(0.16f, 0.17f, 0.18f),
                        0.05f,
                        0.28f);
                }
            }
            if (_siteGrassMaterial == null)
                _siteGrassMaterial = CreateSiteMaterial(
                    "Site-Grass",
                    new Color(0.18f, 0.30f, 0.16f),
                    0f,
                    0.18f);
            if (_siteConcreteMaterial == null)
                _siteConcreteMaterial = CreateSiteMaterial(
                    "Site-Concrete",
                    new Color(0.52f, 0.54f, 0.54f),
                    0f,
                    0.22f);
            if (_siteMarkingMaterial == null)
                _siteMarkingMaterial = CreateSiteMaterial(
                    "Site-RoadMarking",
                    new Color(0.88f, 0.87f, 0.76f),
                    0f,
                    0.18f);
            if (_siteFireZoneMaterial == null)
                _siteFireZoneMaterial = CreateSiteMaterial(
                    "Site-FireZone",
                    new Color(0.72f, 0.09f, 0.055f),
                    0f,
                    0.24f);
            if (_siteGlassMaterial == null)
                _siteGlassMaterial = CreateSiteMaterial(
                    "Site-LobbyGlass",
                    new Color(0.06f, 0.16f, 0.22f),
                    0.15f,
                    0.68f);
            if (_siteMetalMaterial == null)
                _siteMetalMaterial = CreateSiteMaterial(
                    "Site-Metal",
                    new Color(0.16f, 0.18f, 0.20f),
                    0.58f,
                    0.52f);
            if (_siteWoodMaterial == null)
                _siteWoodMaterial = CreateSiteMaterial(
                    "Site-Wood",
                    new Color(0.34f, 0.17f, 0.075f),
                    0f,
                    0.30f);
            if (_siteFoliageMaterial == null)
                _siteFoliageMaterial = CreateSiteMaterial(
                    "Site-Foliage",
                    new Color(0.10f, 0.29f, 0.13f),
                    0f,
                    0.16f);
        }

        private static Material CreateSiteMaterial(
            string name,
            Color color,
            float metallic,
            float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader);
            material.name = name;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        private void BuildCellSurfaceMesh(
            string name,
            IReadOnlyList<(int X, int Y)> cells,
            float height,
            Material material,
            float uvScale)
        {
            var vertices = new List<Vector3>(cells.Count * 4);
            var triangles = new List<int>(cells.Count * 6);
            var uv = new List<Vector2>(cells.Count * 4);
            for (int index = 0; index < cells.Count; index++)
            {
                (int X, int Y) cell = cells[index];
                int vertex = vertices.Count;
                float minX = cell.X - 0.5f;
                float maxX = cell.X + 0.5f;
                float minY = cell.Y - 0.5f;
                float maxY = cell.Y + 0.5f;
                vertices.Add(new Vector3(minX, height, minY));
                vertices.Add(new Vector3(maxX, height, minY));
                vertices.Add(new Vector3(maxX, height, maxY));
                vertices.Add(new Vector3(minX, height, maxY));
                triangles.Add(vertex);
                triangles.Add(vertex + 2);
                triangles.Add(vertex + 1);
                triangles.Add(vertex);
                triangles.Add(vertex + 3);
                triangles.Add(vertex + 2);
                uv.Add(new Vector2(minX * uvScale, minY * uvScale));
                uv.Add(new Vector2(maxX * uvScale, minY * uvScale));
                uv.Add(new Vector2(maxX * uvScale, maxY * uvScale));
                uv.Add(new Vector2(minX * uvScale, maxY * uvScale));
            }
            var surface = new GameObject(name);
            Track(surface, SimulationVisualLayer.ThreeDimensional);
            MeshFilter filter = surface.AddComponent<MeshFilter>();
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
            var mesh = new Mesh { name = name + "-Mesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            _siteRoadMesh = mesh;
        }

        private void BuildSiteBoundary()
        {
            float width = _problem.Width + 2f;
            float depth = _problem.Height + 2f;
            CreateSitePrimitive(
                PrimitiveType.Cube,
                "SiteBoundary-South",
                new Vector3((_problem.Width - 1) * 0.5f, 0f, -1f),
                new Vector3(width, 0.16f, 0.18f),
                _siteConcreteMaterial);
            CreateSitePrimitive(
                PrimitiveType.Cube,
                "SiteBoundary-North",
                new Vector3(
                    (_problem.Width - 1) * 0.5f,
                    0f,
                    _problem.Height),
                new Vector3(width, 0.16f, 0.18f),
                _siteConcreteMaterial);
            CreateSitePrimitive(
                PrimitiveType.Cube,
                "SiteBoundary-West",
                new Vector3(-1f, 0f, (_problem.Height - 1) * 0.5f),
                new Vector3(0.18f, 0.16f, depth),
                _siteConcreteMaterial);
            CreateSitePrimitive(
                PrimitiveType.Cube,
                "SiteBoundary-East",
                new Vector3(
                    _problem.Width,
                    0f,
                    (_problem.Height - 1) * 0.5f),
                new Vector3(0.18f, 0.16f, depth),
                _siteConcreteMaterial);
        }

        private void BuildParkingSpaceMarkings()
        {
            foreach (ParkingSlotV2 slot in _problem.Slots)
            {
                VehiclePose pose = slot.Pose;
                var second = pose.SecondCell;
                Vector3 center = new Vector3(
                    (pose.X + second.X) * 0.5f,
                    RoadSurfaceHeight + 0.012f,
                    (pose.Y + second.Y) * 0.5f);
                bool horizontal =
                    pose.Orientation == VehicleOrientation.Horizontal;
                float halfLength = 0.98f;
                float halfWidth = 0.44f;
                Vector3 longScale = horizontal
                    ? new Vector3(1.96f, 0.012f, 0.035f)
                    : new Vector3(0.035f, 0.012f, 1.96f);
                Vector3 shortScale = horizontal
                    ? new Vector3(0.035f, 0.012f, 0.88f)
                    : new Vector3(0.88f, 0.012f, 0.035f);
                Vector3 longOffset = horizontal
                    ? new Vector3(0f, 0f, halfWidth)
                    : new Vector3(halfWidth, 0f, 0f);
                Vector3 shortOffset = horizontal
                    ? new Vector3(halfLength, 0f, 0f)
                    : new Vector3(0f, 0f, halfLength);
                string prefix = "ParkingSpace-" + slot.Id + "-";
                CreateSitePrimitive(
                    PrimitiveType.Cube,
                    prefix + "SideA",
                    center + longOffset,
                    longScale,
                    _siteMarkingMaterial);
                CreateSitePrimitive(
                    PrimitiveType.Cube,
                    prefix + "SideB",
                    center - longOffset,
                    longScale,
                    _siteMarkingMaterial);
                CreateSitePrimitive(
                    PrimitiveType.Cube,
                    prefix + "EndA",
                    center + shortOffset,
                    shortScale,
                    _siteMarkingMaterial);
                CreateSitePrimitive(
                    PrimitiveType.Cube,
                    prefix + "EndB",
                    center - shortOffset,
                    shortScale,
                    _siteMarkingMaterial);
            }
        }

        private void BuildRoadLaneMarkings()
        {
            foreach (int y in new[] { 3, 18, 37 })
            {
                for (int x = 1; x < _problem.Width - 1; x += 4)
                {
                    if (!_problem.IsFloor(x, y)) continue;
                    CreateSitePrimitive(
                        PrimitiveType.Cube,
                        "LaneDash-" + y + "-" + x,
                        new Vector3(
                            x,
                            RoadSurfaceHeight + 0.014f,
                            y),
                        new Vector3(1.65f, 0.014f, 0.045f),
                        _siteMarkingMaterial);
                }
            }
            if (_complex == null) return;
            foreach (ApartmentComplexEntranceV2 entrance in _complex.Entrances)
            {
                for (int stripe = -2; stripe <= 2; stripe++)
                {
                    CreateSitePrimitive(
                        PrimitiveType.Cube,
                        "Crosswalk-" + entrance.Name + "-" + stripe,
                        new Vector3(
                            entrance.Cell.X + stripe * 0.28f,
                            RoadSurfaceHeight + 0.016f,
                            entrance.Cell.Y),
                        new Vector3(0.14f, 0.014f, 1.6f),
                        _siteMarkingMaterial);
                }
            }
        }

        private void BuildFireEngineZoneMarkings()
        {
            if (_complex == null) return;
            foreach (ApartmentBuildingV2 building in _complex.Buildings)
            {
                int minX = building.FireEngineZone.Cells.Min(cell => cell.X);
                int maxX = building.FireEngineZone.Cells.Max(cell => cell.X);
                int minY = building.FireEngineZone.Cells.Min(cell => cell.Y);
                int maxY = building.FireEngineZone.Cells.Max(cell => cell.Y);
                float centerX = (minX + maxX) * 0.5f;
                float centerY = (minY + maxY) * 0.5f;
                float width = maxX - minX + 0.90f;
                float depth = maxY - minY + 0.90f;
                float line = 0.075f;
                float height = RoadSurfaceHeight + 0.018f;
                string prefix = "FireEngineZone-" + building.Id + "-";
                CreateSitePrimitive(
                    PrimitiveType.Cube,
                    prefix + "West",
                    new Vector3(centerX - width * 0.5f, height, centerY),
                    new Vector3(line, 0.016f, depth),
                    _siteFireZoneMaterial);
                CreateSitePrimitive(
                    PrimitiveType.Cube,
                    prefix + "East",
                    new Vector3(centerX + width * 0.5f, height, centerY),
                    new Vector3(line, 0.016f, depth),
                    _siteFireZoneMaterial);
                CreateSitePrimitive(
                    PrimitiveType.Cube,
                    prefix + "South",
                    new Vector3(centerX, height, centerY - depth * 0.5f),
                    new Vector3(width, 0.016f, line),
                    _siteFireZoneMaterial);
                CreateSitePrimitive(
                    PrimitiveType.Cube,
                    prefix + "North",
                    new Vector3(centerX, height, centerY + depth * 0.5f),
                    new Vector3(width, 0.016f, line),
                    _siteFireZoneMaterial);
            }
        }

        private void BuildLandscapeProps()
        {
            Vector3[] treePositions =
            {
                new Vector3(1f, 0f, 8f),
                new Vector3(1f, 0f, 30f),
                new Vector3(57f, 0f, 8f),
                new Vector3(57f, 0f, 30f),
                new Vector3(8f, 0f, 40f),
                new Vector3(22f, 0f, 40f),
                new Vector3(35f, 0f, 40f),
                new Vector3(48f, 0f, 40f),
            };
            for (int index = 0; index < treePositions.Length; index++)
                BuildTree("LandscapeTree-" + (index + 1), treePositions[index]);

            foreach (int x in new[] { 9, 22, 35, 48 })
            {
                BuildBench(
                    "CourtyardBench-North-" + x,
                    new Vector3(x + 2.4f, 0f, 35.25f),
                    180f);
                BuildBench(
                    "CourtyardBench-South-" + x,
                    new Vector3(x - 2.4f, 0f, 5.25f),
                    0f);
                BuildLamp(
                    "LobbyLamp-North-" + x,
                    new Vector3(x - 2.1f, 0f, 20.55f));
                BuildLamp(
                    "LobbyLamp-South-" + x,
                    new Vector3(x + 2.1f, 0f, 15.45f));
            }
        }

        private void BuildTree(string name, Vector3 position)
        {
            var root = new GameObject(name);
            Track(root, SimulationVisualLayer.ThreeDimensional);
            root.transform.position = position;
            GameObject trunk = CreateSitePrimitive(
                PrimitiveType.Cylinder,
                name + "-Trunk",
                position + Vector3.up * 0.54f,
                new Vector3(0.13f, 0.54f, 0.13f),
                _siteWoodMaterial,
                root.transform);
            GameObject crown = CreateSitePrimitive(
                PrimitiveType.Sphere,
                name + "-Crown",
                position + Vector3.up * 1.48f,
                new Vector3(0.92f, 1.12f, 0.92f),
                _siteFoliageMaterial,
                root.transform);
            trunk.transform.position = position + Vector3.up * 0.54f;
            crown.transform.position = position + Vector3.up * 1.48f;
        }

        private void BuildBench(string name, Vector3 position, float yaw)
        {
            var root = new GameObject(name);
            Track(root, SimulationVisualLayer.ThreeDimensional);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            CreateSitePrimitive(
                PrimitiveType.Cube,
                name + "-Seat",
                new Vector3(0f, 0.38f, 0f),
                new Vector3(1.25f, 0.12f, 0.38f),
                _siteWoodMaterial,
                root.transform,
                true);
            CreateSitePrimitive(
                PrimitiveType.Cube,
                name + "-Back",
                new Vector3(0f, 0.72f, 0.16f),
                new Vector3(1.25f, 0.52f, 0.10f),
                _siteWoodMaterial,
                root.transform,
                true);
            foreach (float x in new[] { -0.45f, 0.45f })
            {
                CreateSitePrimitive(
                    PrimitiveType.Cube,
                    name + "-Leg-" + x,
                    new Vector3(x, 0.18f, 0f),
                    new Vector3(0.09f, 0.36f, 0.30f),
                    _siteMetalMaterial,
                    root.transform,
                    true);
            }
        }

        private void BuildLamp(string name, Vector3 position)
        {
            var root = new GameObject(name);
            Track(root, SimulationVisualLayer.ThreeDimensional);
            root.transform.position = position;
            CreateSitePrimitive(
                PrimitiveType.Cylinder,
                name + "-Pole",
                new Vector3(0f, 1.45f, 0f),
                new Vector3(0.045f, 1.45f, 0.045f),
                _siteMetalMaterial,
                root.transform,
                true);
            CreateSitePrimitive(
                PrimitiveType.Sphere,
                name + "-Light",
                new Vector3(0f, 2.92f, 0f),
                new Vector3(0.18f, 0.14f, 0.18f),
                _siteMarkingMaterial,
                root.transform,
                true);
        }

        private void BuildRouteOverlays()
        {
            if (_candidateRoutes == null || _selectedRoute == null) return;
            foreach (EmergencyAccessRouteV2 route in _candidateRoutes)
            {
                if (route.Name == _selectedRoute.Name) continue;
                BuildRouteOverlay(
                    route,
                    "CandidateRoute-" + route.Name,
                    new Color(0.10f, 0.34f, 0.82f),
                    0.005f,
                    0.58f);
            }
            BuildRouteOverlay(
                _selectedRoute,
                "SelectedRoute-" + _selectedRoute.Name,
                new Color(0.02f, 0.92f, 0.94f),
                0.025f,
                0.90f,
                SimulationVisualLayer.Control);
            BuildThreeDimensionalRouteBoundary(_selectedRoute);
        }

        private void BuildApartmentContext()
        {
            if (_complex == null) return;
            foreach (ApartmentBuildingV2 apartment in _complex.Buildings)
            {
                int minX = apartment.FootprintCells.Min(cell => cell.X);
                int maxX = apartment.FootprintCells.Max(cell => cell.X);
                int minY = apartment.FootprintCells.Min(cell => cell.Y);
                int maxY = apartment.FootprintCells.Max(cell => cell.Y);
                float width = maxX - minX + 0.82f;
                float depth = maxY - minY + 0.82f;
                float height = 8.5f + ((apartment.Id - 101) % 4) * 0.9f;
                Vector3 origin = new Vector3(
                    (minX + maxX) * 0.5f,
                    0f,
                    (minY + maxY) * 0.5f);
                BuildApartmentGroundDetails(
                    apartment,
                    origin,
                    width,
                    depth,
                    minY,
                    maxY);
                BuildApartmentBuilding(
                    apartment.Id + "-Apartment",
                    origin,
                    width,
                    depth,
                    height,
                    variant: apartment.Id - 101);
            }
        }

        private void BuildApartmentGroundDetails(
            ApartmentBuildingV2 apartment,
            Vector3 origin,
            float width,
            float depth,
            int minY,
            int maxY)
        {
            CreateSitePrimitive(
                PrimitiveType.Cube,
                "ApartmentPodium-" + apartment.Id,
                new Vector3(origin.x, 0f, origin.z),
                new Vector3(width + 0.10f, 0.08f, depth + 0.10f),
                _siteConcreteMaterial);

            bool northRow = apartment.Id <= 104;
            float direction = northRow ? -1f : 1f;
            float facadeZ = northRow
                ? minY + 0.10f
                : maxY - 0.10f;
            float lobbyOutsideZ = facadeZ + direction * 0.42f;
            CreateSitePrimitive(
                PrimitiveType.Cube,
                "LobbyDoor-" + apartment.Id,
                new Vector3(origin.x, 1.02f, facadeZ + direction * 0.03f),
                new Vector3(1.32f, 2.04f, 0.08f),
                _siteGlassMaterial);
            CreateSitePrimitive(
                PrimitiveType.Cube,
                "LobbyCanopy-" + apartment.Id,
                new Vector3(origin.x, 2.22f, lobbyOutsideZ),
                new Vector3(2.55f, 0.14f, 0.92f),
                _siteMetalMaterial);
            foreach (float side in new[] { -1f, 1f })
            {
                CreateSitePrimitive(
                    PrimitiveType.Cube,
                    "LobbyColumn-" + apartment.Id + "-" + side,
                    new Vector3(
                        origin.x + side * 1.02f,
                        1.08f,
                        lobbyOutsideZ + direction * 0.28f),
                    new Vector3(0.12f, 2.16f, 0.12f),
                    _siteMetalMaterial);
            }
            CreateSitePrimitive(
                PrimitiveType.Cube,
                "LobbyWalk-" + apartment.Id,
                new Vector3(
                    origin.x,
                    RoadSurfaceHeight + 0.035f,
                    facadeZ + direction * 0.66f),
                new Vector3(2.35f, 0.07f, 1.28f),
                _siteConcreteMaterial);
            BuildLobbySign(
                apartment.Id,
                new Vector3(
                    origin.x,
                    2.62f,
                    facadeZ + direction * 0.10f),
                northRow ? 180f : 0f);
        }

        private void BuildLobbySign(
            int buildingId,
            Vector3 position,
            float yaw)
        {
            var sign = new GameObject("LobbySign-" + buildingId);
            Track(sign, SimulationVisualLayer.ThreeDimensional);
            sign.transform.position = position;
            sign.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            TextMesh text = sign.AddComponent<TextMesh>();
            text.text = buildingId + "동";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.24f;
            text.fontSize = 48;
            text.color = new Color(0.92f, 0.94f, 0.98f);
            _threeDimensionalLabels.Add(text);
        }

        private void BuildApartmentBuilding(
            string name,
            Vector3 origin,
            float width,
            float depth,
            float height,
            int variant)
        {
            GameObject building =
                SimulationVisualAssetFactory.TryCreateApartment(variant, name);
            if (building == null)
            {
                Debug.LogWarning(
                    "[Model V2] " + name +
                    " 도시 에셋이 없어 합성 건물을 만들지 않음");
                return;
            }
            Track(building, SimulationVisualLayer.ThreeDimensional);
            FitVisualToBounds(
                building,
                origin,
                new Vector3(width, height, depth));
            DisableColliders(building);
            BuildApartmentClickTarget(
                name,
                origin,
                new Vector3(width, height, depth));
            // 동 번호는 현관 로비 사인(BuildLobbySign) 하나만 사용 — 상단 대형
            // 라벨은 중복 표기라 제거 (사용자 피드백).
        }

        private void BuildApartmentClickTarget(
            string name,
            Vector3 bottomCenter,
            Vector3 size)
        {
            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Track(target, SimulationVisualLayer.ThreeDimensional);
            target.name = name + "-ClickTarget";
            target.transform.position =
                bottomCenter + Vector3.up * (size.y * 0.5f);
            target.transform.localScale = size;
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
        }

        private static void FitVisualToBounds(
            GameObject visual,
            Vector3 targetBottomCenter,
            Vector3 targetSize)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                visual.transform.position = targetBottomCenter;
                return;
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            visual.transform.localScale = new Vector3(
                targetSize.x / Mathf.Max(0.001f, bounds.size.x),
                targetSize.y / Mathf.Max(0.001f, bounds.size.y),
                targetSize.z / Mathf.Max(0.001f, bounds.size.z));
            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            visual.transform.position += new Vector3(
                targetBottomCenter.x - bounds.center.x,
                targetBottomCenter.y - bounds.min.y,
                targetBottomCenter.z - bounds.center.z);
        }

        private void BuildRouteOverlay(
            EmergencyAccessRouteV2 route,
            string namePrefix,
            Color color,
            float y,
            float scale,
            SimulationVisualLayer layer = SimulationVisualLayer.Control)
        {
            int index = 0;
            foreach (var cell in route.RequiredCells)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Track(marker, layer);
                marker.name = namePrefix + "-" + index++;
                marker.transform.position = new Vector3(cell.X, y, cell.Y);
                marker.transform.localScale = new Vector3(scale, 0.018f, scale);
                SetColor(marker, color);
                Collider collider = marker.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
            }
        }

        private void BuildThreeDimensionalRouteBoundary(
            EmergencyAccessRouteV2 route)
        {
            var cells = new HashSet<(int X, int Y)>(route.RequiredCells);
            Color lineColor = new Color(1f, 0.63f, 0.04f);
            int index = 0;
            foreach (var cell in route.RequiredCells)
            {
                if (!cells.Contains((cell.X - 1, cell.Y)))
                    BuildRoadBoundarySegment(
                        "FireLane-West-" + index,
                        new Vector3(cell.X - 0.47f, 0.015f, cell.Y),
                        new Vector3(0.055f, 0.014f, 0.94f),
                        lineColor);
                if (!cells.Contains((cell.X + 1, cell.Y)))
                    BuildRoadBoundarySegment(
                        "FireLane-East-" + index,
                        new Vector3(cell.X + 0.47f, 0.015f, cell.Y),
                        new Vector3(0.055f, 0.014f, 0.94f),
                        lineColor);
                if (!cells.Contains((cell.X, cell.Y - 1)))
                    BuildRoadBoundarySegment(
                        "FireLane-South-" + index,
                        new Vector3(cell.X, 0.015f, cell.Y - 0.47f),
                        new Vector3(0.94f, 0.014f, 0.055f),
                        lineColor);
                if (!cells.Contains((cell.X, cell.Y + 1)))
                    BuildRoadBoundarySegment(
                        "FireLane-North-" + index,
                        new Vector3(cell.X, 0.015f, cell.Y + 0.47f),
                        new Vector3(0.94f, 0.014f, 0.055f),
                        lineColor);
                index++;
            }
        }

        private void BuildRoadBoundarySegment(
            string name,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Track(segment, SimulationVisualLayer.ThreeDimensional);
            segment.name = name;
            segment.transform.position = position;
            segment.transform.localScale = scale;
            SetColor(segment, color);
            Collider collider = segment.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        private void BuildFireMarker()
        {
            if (_fireBuilding == null) return;
            bool northRow = _fireBuilding.Id <= 104;
            int facadeY = northRow
                ? _fireBuilding.FootprintCells.Max(cell => cell.Y)
                : _fireBuilding.FootprintCells.Min(cell => cell.Y);
            GameObject fire = SimulationVisualAssetFactory.TryCreateFire(
                "Building-Fire-" + _fireBuilding.Id);
            _fireUsesCustomView = fire != null;
            if (fire == null)
                fire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Track(fire, SimulationVisualLayer.Shared);
            fire.name = "Building-Fire-" + _fireBuilding.Id;
            fire.transform.position = new Vector3(
                _fireBuilding.FireEngineZone.ApproachCell.X,
                4.8f,
                facadeY + (northRow ? 0.48f : -0.48f));
            if (_fireUsesCustomView)
            {
                fire.transform.localScale = Vector3.one * 1.25f;
                DisableColliders(fire);
            }
            else
            {
                fire.transform.localScale = new Vector3(0.34f, 0.14f, 0.34f);
                SetColor(fire, new Color(1f, 0.08f, 0.02f));
                GameObject flame = CreateChildPrimitive(
                    PrimitiveType.Sphere,
                    fire.transform,
                    "Fire-Flame",
                    new Vector3(0f, 1.45f, 0f),
                    new Vector3(0.62f, 1.8f, 0.62f),
                    new Color(1f, 0.45f, 0.02f));
                Collider flameCollider = flame.GetComponent<Collider>();
                if (flameCollider != null) Destroy(flameCollider);
            }
            _fireMarker = fire;
        }

        private void BuildEntranceMarker()
        {
            if (_selectedEntrance == null) return;
            var gate = new GameObject("Emergency-Entrance");
            Track(gate, SimulationVisualLayer.Shared);
            gate.transform.position = new Vector3(
                _selectedEntrance.Cell.X,
                0f,
                _selectedEntrance.Cell.Y);
            CreateChildPrimitive(
                PrimitiveType.Cube, gate.transform, "Entrance-Left",
                new Vector3(0f, 0.52f, -1f),
                new Vector3(0.18f, 1.05f, 0.18f), Color.white);
            CreateChildPrimitive(
                PrimitiveType.Cube, gate.transform, "Entrance-Right",
                new Vector3(0f, 0.52f, 1f),
                new Vector3(0.18f, 1.05f, 0.18f), Color.white);
            CreateChildPrimitive(
                PrimitiveType.Cube, gate.transform, "Entrance-Header",
                new Vector3(0f, 1.02f, 0f),
                new Vector3(0.18f, 0.14f, 2.18f), new Color(0.12f, 0.82f, 1f));
        }

    }
}
