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
        private void BuildFixedCars()
        {
            for (int i = 0; i < _problem.FixedVehiclePoses.Count; i++)
            {
                VehiclePose pose = _problem.FixedVehiclePoses[i];
                GameObject car = CreateCar(
                    "FixedVehicle-" + (i + 1),
                    pose,
                    i);
                SetCarBodyColor(car, VehicleBodyColor(i));
            }
        }

        private void BuildMovableCars()
        {
            for (int vehicle = 0; vehicle < _problem.VehicleCount; vehicle++)
            {
                VehiclePose pose = _problem.Slots[_problem.InitialVehicleSlots[vehicle]].Pose;
                GameObject car = CreateCar(
                    "MovableVehicle-" + (vehicle + 1),
                    pose,
                    vehicle + _problem.FixedVehiclePoses.Count);
                SetCarBodyColor(
                    car,
                    VehicleBodyColor(
                        vehicle + _problem.FixedVehiclePoses.Count));
                _carViews.Add(vehicle, car);
                _carTrackingFrames.Add(
                    vehicle,
                    BuildVehicleTrackingFrame(vehicle, pose));
                BuildControlMovableVehicleMarker(vehicle, pose);
            }
        }

        private static Mesh _plumbobMesh;

        /// <summary>아래로 뾰족한 8면체(플럼밥) — 대상 차량 상공 부유 마커용</summary>
        private static Mesh PlumbobMesh()
        {
            if (_plumbobMesh != null) return _plumbobMesh;
            Vector3 top = new Vector3(0f, 0.20f, 0f);
            Vector3 bottom = new Vector3(0f, -0.30f, 0f);
            Vector3[] ring =
            {
                new Vector3(0.15f, 0f, 0f),
                new Vector3(0f, 0f, 0.15f),
                new Vector3(-0.15f, 0f, 0f),
                new Vector3(0f, 0f, -0.15f),
            };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int index = 0; index < 4; index++)
            {
                int next = (index + 1) % 4;
                // 평평한 면 셰이딩을 위해 삼각형마다 정점을 복제한다
                triangles.Add(vertices.Count); vertices.Add(top);
                triangles.Add(vertices.Count); vertices.Add(ring[next]);
                triangles.Add(vertices.Count); vertices.Add(ring[index]);
                triangles.Add(vertices.Count); vertices.Add(bottom);
                triangles.Add(vertices.Count); vertices.Add(ring[index]);
                triangles.Add(vertices.Count); vertices.Add(ring[next]);
            }
            var mesh = new Mesh { name = "Plumbob" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _plumbobMesh = mesh;
            return mesh;
        }

        /// <summary>플럼밥용 투명 글래스 — 반투명 URP Lit, 높은 매끄러움.
        /// 틴트는 SetTrackingFrameColor가 알파를 보존하며 입힌다.</summary>
        private static Material CreatePlumbobGlassMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader);
            material.name = "PlumbobGlass";
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat(
                    "_SrcBlend",
                    (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat(
                    "_DstBlend",
                    (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue =
                (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0.10f);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.94f);
            Color baseColor = new Color(1f, 1f, 1f, 0.60f);
            material.color = baseColor;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", baseColor);
            return material;
        }

        private GameObject BuildVehicleTrackingFrame(
            int vehicle,
            VehiclePose pose)
        {
            var marker = new GameObject(
                "MoveTargetMarker-" + (vehicle + 1));
            Track(marker, SimulationVisualLayer.ThreeDimensional);
            var gem = new GameObject("Plumbob");
            gem.transform.SetParent(marker.transform, worldPositionStays: false);
            MeshFilter filter = gem.AddComponent<MeshFilter>();
            filter.sharedMesh = PlumbobMesh();
            MeshRenderer renderer = gem.AddComponent<MeshRenderer>();
            renderer.material = CreatePlumbobGlassMaterial();
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            marker.transform.position = VehiclePosition(pose, false, false);
            SetTrackingFrameColor(
                marker,
                new Color(1f, 0.48f, 0.04f));
            return marker;
        }

        private void BuildControlMovableVehicleMarker(
            int vehicle,
            VehiclePose pose)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Track(marker, SimulationVisualLayer.Control);
            marker.name = "MoveTarget-" + (vehicle + 1);
            var second = pose.SecondCell;
            marker.transform.position = new Vector3(
                (pose.X + second.X) / 2f,
                0.052f,
                (pose.Y + second.Y) / 2f);
            marker.transform.localScale =
                pose.Orientation == VehicleOrientation.Horizontal
                    ? new Vector3(2.18f, 0.018f, 1.08f)
                    : new Vector3(1.08f, 0.018f, 2.18f);
            SetColor(marker, new Color(1f, 0.43f, 0.04f));
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        private GameObject CreateCar(
            string name,
            VehiclePose pose,
            int visualVariant)
        {
            GameObject customVisual =
                SimulationVisualAssetFactory.TryCreateCar(
                    visualVariant,
                    name + "-Visual");
            bool primitiveFallback = customVisual == null;
            GameObject car = primitiveFallback
                ? GameObject.CreatePrimitive(PrimitiveType.Cube)
                : new GameObject(name);
            Track(car, SimulationVisualLayer.Shared);
            car.name = name;
            if (customVisual != null)
            {
                customVisual.transform.SetParent(
                    car.transform,
                    worldPositionStays: true);
                customVisual.transform.localRotation = Quaternion.identity;
                FitVisualToBounds(
                    customVisual,
                    Vector3.zero,
                    new Vector3(0.82f, 0.62f, 1.82f));
                customVisual.transform.localRotation =
                    Quaternion.Euler(0f, 90f, 0f);
                AlignVisualToBottomCenter(
                    customVisual,
                    new Vector3(0f, CustomVehicleBottomOffset, 0f));
                DisableColliders(customVisual);
            }
            else
            {
                car.transform.localScale =
                    new Vector3(1.82f, 0.42f, 0.82f);
            }
            car.transform.position = VehiclePosition(pose, false, false);
            car.transform.rotation = VehicleRotation(pose);
            if (!primitiveFallback) return car;
            CreateChildPrimitive(
                PrimitiveType.Cube, car.transform, name + "-Cabin",
                new Vector3(0.02f, 0.72f, 0f),
                new Vector3(0.48f, 0.70f, 0.74f),
                new Color(0.12f, 0.22f, 0.30f));
            CreateChildPrimitive(
                PrimitiveType.Cube, car.transform, name + "-Front",
                new Vector3(0.43f, 0.05f, 0f),
                new Vector3(0.06f, 0.54f, 0.76f),
                new Color(0.92f, 0.92f, 0.78f));
            return car;
        }

        private static void AlignVisualToBottomCenter(
            GameObject visual,
            Vector3 targetBottomCenter)
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
            visual.transform.position += new Vector3(
                targetBottomCenter.x - bounds.center.x,
                targetBottomCenter.y - bounds.min.y,
                targetBottomCenter.z - bounds.center.z);
        }

        private void BuildRobots()
        {
            // 계획이 활성화한 유닛(규칙 9)보다 보유 유닛이 많으면, 나머지는
            // 차고에 정차한 모습으로 함께 표시한다 — 이동 0대 계획에서도
            // 유닛이 사라져 보이지 않게(단지 전환 시 혼동 방지).
            int viewCount = Mathf.Clamp(
                Mathf.Max(_plan.RobotTimelines.Length, _availableUnitCount),
                1,
                _problem.RobotStarts.Count);
            _robotViews = new GameObject[viewCount];
            _robotControlMarkers = new GameObject[viewCount];
            _robotControlLabels = new TextMesh[viewCount];
            _robotServiceIndicators = new GameObject[viewCount];
            _robotLiftVisuals = new TransportLiftVisual[viewCount];
            _robotUsesCustomView = new bool[viewCount];
            _transportCameras = new Camera[viewCount];
            _transportCameraFocusOffsets = new Vector3[viewCount];
            _transportCameraYaws = new float[viewCount];
            _transportCameraPitches = new float[viewCount];
            _transportCameraDistances = new float[viewCount];
            _swerveOffsets = new Vector3[viewCount];
            for (int robot = 0; robot < viewCount; robot++)
            {
                GameObject cube = SimulationVisualAssetFactory.TryCreate(
                    SimulationVisualAssetFactory.TransportUnitResourcePath,
                    "TransportUnit-" + (robot + 1));
                bool primitiveFallback = cube == null;
                if (primitiveFallback)
                    cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Track(cube, SimulationVisualLayer.Shared);
                cube.name = "TransportUnit-" + (robot + 1);
                if (primitiveFallback)
                {
                    cube.transform.localScale = new Vector3(0.72f, 0.18f, 0.72f);
                    SetColor(cube, RobotColor(robot, false));
                }
                else
                {
                    cube.transform.localScale = Vector3.one;
                }
                _robotViews[robot] = cube;
                _robotControlMarkers[robot] =
                    BuildRobotControlMarker(robot);
                _robotUsesCustomView[robot] = !primitiveFallback;
                if (primitiveFallback)
                {
                    CreateChildPrimitive(
                        PrimitiveType.Cube, cube.transform, "Platform-" + (robot + 1),
                        new Vector3(0f, 0.72f, 0f),
                        new Vector3(1.12f, 0.26f, 1.12f),
                        new Color(0.10f, 0.12f, 0.15f));
                }
                _robotLiftVisuals[robot] =
                    BuildTransportLiftVisual(cube.transform, robot);
                GameObject indicator = CreateChildPrimitive(
                    PrimitiveType.Sphere, cube.transform, "ServiceLight-" + (robot + 1),
                    primitiveFallback
                        ? new Vector3(0f, 1.55f, 0f)
                        : new Vector3(0f, 0.16f, 0f),
                    primitiveFallback
                        ? new Vector3(0.30f, 0.55f, 0.30f)
                        : new Vector3(0.12f, 0.18f, 0.12f),
                    new Color(1f, 0.72f, 0.08f));
                Collider collider = indicator.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                indicator.SetActive(false);
                _robotServiceIndicators[robot] = indicator;
                _transportCameras[robot] =
                    BuildTransportCamera(cube.transform, robot);
                _transportCameraFocusOffsets[robot] =
                    new Vector3(0f, 0.12f, 0f);
                _transportCameraYaws[robot] = 90f;
                _transportCameraPitches[robot] = 25f;
                _transportCameraDistances[robot] = 2.2f;
                if (robot >= _plan.RobotTimelines.Length)
                {
                    // 정적 도크 유닛 — 차고 위치에 밀착 상태로 정차
                    var dock = _problem.RobotStarts[robot];
                    cube.transform.position = new Vector3(
                        dock.X,
                        _robotUsesCustomView[robot] ? 0f : 0.20f,
                        dock.Y);
                    cube.transform.rotation = Quaternion.identity;
                    TransportLiftVisual liftVisual = _robotLiftVisuals[robot];
                    if (liftVisual != null && liftVisual.AxleModules != null)
                        for (int module = 0;
                             module < liftVisual.AxleModules.Length;
                             module++)
                        {
                            if (liftVisual.AxleModules[module] == null) continue;
                            Vector3 local =
                                liftVisual.AxleModules[module].localPosition;
                            liftVisual.AxleModules[module].localPosition =
                                new Vector3(
                                    (module == 0 ? -1f : 1f) * IdleModuleOffsetX,
                                    local.y,
                                    local.z);
                        }
                    if (_robotControlMarkers[robot] != null)
                        _robotControlMarkers[robot].transform.position =
                            new Vector3(dock.X, 0.30f, dock.Y);
                }
                ApplyTransportCameraPose(robot);
            }
        }

        private GameObject BuildRobotControlMarker(int robot)
        {
            var marker = new GameObject(
                "Control-RobotHighlight-" + (robot + 1));
            Track(marker, SimulationVisualLayer.Control);
            LineRenderer line = marker.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 12;
            line.startWidth = 0.085f;
            line.endWidth = 0.085f;
            line.numCornerVertices = 2;
            line.material = CreateTrackingLineMaterial();
            line.sortingOrder = 20;
            const float radius = 0.72f;
            for (int index = 0; index < line.positionCount; index++)
            {
                float angle =
                    index * Mathf.PI * 2f / line.positionCount;
                line.SetPosition(
                    index,
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        0f,
                        Mathf.Sin(angle) * radius));
            }

            var labelObject = new GameObject(
                "Control-RobotLabel-" + (robot + 1));
            labelObject.transform.SetParent(
                marker.transform,
                worldPositionStays: false);
            labelObject.transform.localPosition =
                new Vector3(0f, 0.035f, 0f);
            labelObject.transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = "R" + (robot + 1);
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 64;
            label.characterSize = 0.16f;
            label.fontStyle = FontStyle.Bold;
            _robotControlLabels[robot] = label;
            SetTrackingFrameColor(
                marker,
                RobotColor(robot, false));
            label.color = RobotColor(robot, false);
            return marker;
        }

        private TransportLiftVisual BuildTransportLiftVisual(
            Transform transport,
            int robot)
        {
            SetRendererEnabledByName(transport, "LiftPlate", false);
            SetSubtreeRenderersEnabledByName(
                transport,
                "VisualOffset",
                false);
            var assembly = new GameObject(
                "PairedAxleModules-" + (robot + 1));
            assembly.transform.SetParent(transport, worldPositionStays: false);
            assembly.transform.localPosition = Vector3.zero;
            assembly.transform.localRotation = Quaternion.identity;

            var decks = new Transform[2];
            var deckRestPositions = new Vector3[2];
            var axleModules = new Transform[2];
            var armPivots = new Transform[8];
            var armRestRotations = new Quaternion[8];
            var armLiftRotations = new Quaternion[8];
            int armIndex = 0;
            for (int moduleIndex = 0; moduleIndex < 2; moduleIndex++)
            {
                float moduleX = moduleIndex == 0 ? -0.54f : 0.54f;
                string moduleName =
                    moduleIndex == 0 ? "RearAxleModule" : "FrontAxleModule";
                var module = new GameObject(moduleName);
                module.transform.SetParent(
                    assembly.transform,
                    worldPositionStays: false);
                module.transform.localPosition =
                    new Vector3(moduleX, 0f, 0f);
                module.transform.localRotation = Quaternion.identity;
                axleModules[moduleIndex] = module.transform;
                CreateChildPrimitive(
                    PrimitiveType.Cube,
                    module.transform,
                    moduleName + "-Body",
                    new Vector3(0f, 0.02f, 0f),
                    new Vector3(0.30f, 0.065f, 0.50f),
                    new Color(0.10f, 0.12f, 0.15f));
                // 덱 리그: 스케일 없는 빈 노드 아래에 덱 판과 암 피벗을 둔다.
                // 스케일된 큐브의 자식으로 붙이면 암이 부모 비균등 스케일을
                // 상속받아 두께 ~2mm로 짜부라져 보이지 않는다 (기존 버그).
                var deck = new GameObject(moduleName + "-LiftDeckRig");
                deck.transform.SetParent(
                    module.transform, worldPositionStays: false);
                deck.transform.localPosition = new Vector3(0f, 0.061f, 0f);
                deck.transform.localRotation = Quaternion.identity;
                CreateChildPrimitive(
                    PrimitiveType.Cube,
                    deck.transform,
                    moduleName + "-LiftDeck",
                    Vector3.zero,
                    new Vector3(0.27f, 0.022f, 0.45f),
                    new Color(0.20f, 0.23f, 0.27f));
                decks[moduleIndex] = deck.transform;
                deckRestPositions[moduleIndex] =
                    deck.transform.localPosition;
                float ledDirection = moduleIndex == 0 ? -1f : 1f;
                CreateChildPrimitive(
                    PrimitiveType.Cube,
                    module.transform,
                    moduleName + "-StatusStrip",
                    new Vector3(ledDirection * 0.176f, 0.052f, 0f),
                    new Vector3(0.018f, 0.020f, 0.18f),
                    new Color(0.08f, 0.82f, 1f));

                for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                {
                    float side = sideIndex == 0 ? -1f : 1f;
                    string sideName =
                        sideIndex == 0 ? "LeftWheelGrip" : "RightWheelGrip";
                    for (int pairIndex = 0; pairIndex < 2; pairIndex++)
                    {
                        float pivotX = pairIndex == 0 ? -0.065f : 0.065f;
                        // 휠 클램핑 암: 휴지 시 앞·뒤 팔이 몸체를 따라 일자(0°/180°)로
                        // 붙어 있고, 리프트 시 서로 반대 방향으로 90°씩 부채 접듯
                        // 쓸어 돌아 타이어 앞뒤 접지면 밑 횡방향 평행으로 모인다.
                        float restYaw = pairIndex == 0 ? 180f : 0f;
                        float liftYaw = side > 0f ? -90f : 90f;
                        var pivot = new GameObject(
                            moduleName + "-" + sideName + "-Hinge-" +
                            (pairIndex == 0 ? "Rear" : "Front"));
                        pivot.transform.SetParent(
                            deck.transform,
                            worldPositionStays: false);
                        pivot.transform.localPosition =
                            new Vector3(pivotX, 0.032f, side * 0.17f);
                        Quaternion restRotation =
                            Quaternion.Euler(0f, restYaw, 0f);
                        Quaternion liftRotation =
                            Quaternion.Euler(0f, liftYaw, 0f);
                        pivot.transform.localRotation = restRotation;
                        CreateChildPrimitive(
                            PrimitiveType.Cube,
                            pivot.transform,
                            moduleName + "-" + sideName + "-Wiper-" +
                            (pairIndex == 0 ? "Rear" : "Front"),
                            new Vector3(0.11f, 0f, 0f),
                            new Vector3(0.22f, 0.036f, 0.040f),
                            new Color(0.70f, 0.74f, 0.78f));
                        CreateChildPrimitive(
                            PrimitiveType.Cylinder,
                            pivot.transform,
                            moduleName + "-" + sideName + "-HingeCap-" +
                            (pairIndex + 1),
                            Vector3.zero,
                            new Vector3(0.052f, 0.020f, 0.052f),
                            new Color(0.18f, 0.21f, 0.24f));
                        armPivots[armIndex] = pivot.transform;
                        armRestRotations[armIndex] = restRotation;
                        armLiftRotations[armIndex] = liftRotation;
                        armIndex++;
                    }
                }
            }
            DisableColliders(assembly);
            return new TransportLiftVisual
            {
                Decks = decks,
                DeckRestPositions = deckRestPositions,
                ArmPivots = armPivots,
                ArmRestRotations = armRestRotations,
                ArmLiftRotations = armLiftRotations,
                AxleModules = axleModules,
            };
        }

        private static void SetRendererEnabledByName(
            Transform root,
            string objectName,
            bool enabled)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name != objectName) continue;
                Renderer renderer = transforms[index].GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = enabled;
            }
        }

        private static void SetSubtreeRenderersEnabledByName(
            Transform root,
            string objectName,
            bool enabled)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name != objectName) continue;
                Renderer[] renderers =
                    transforms[index].GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0;
                     rendererIndex < renderers.Length;
                     rendererIndex++)
                    renderers[rendererIndex].enabled = enabled;
            }
        }

        private static Camera BuildTransportCamera(
            Transform transport,
            int robotIndex)
        {
            var cameraObject = new GameObject(
                "TransportUnit-Camera-" + (robotIndex + 1));
            cameraObject.transform.SetParent(transport, worldPositionStays: false);
            cameraObject.transform.localPosition = new Vector3(-1.55f, 1.05f, 0f);
            cameraObject.transform.localRotation = Quaternion.LookRotation(
                new Vector3(1.95f, -0.78f, 0f),
                Vector3.up);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.08f;
            camera.farClipPlane = 120f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.04f, 0.07f);
            cameraObject.SetActive(false);
            return camera;
        }

    }
}
