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

        private GameObject BuildVehicleTrackingFrame(
            int vehicle,
            VehiclePose pose)
        {
            var frame = new GameObject(
                "MoveTargetFrame-" + (vehicle + 1));
            Track(frame, SimulationVisualLayer.ThreeDimensional);
            LineRenderer line = frame.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = 16;
            line.startWidth = 0.026f;
            line.endWidth = 0.026f;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.material = CreateTrackingLineMaterial();
            float x = 0.98f;
            float z = 0.48f;
            float bottom = -0.29f;
            float top = 0.42f;
            line.SetPositions(new[]
            {
                new Vector3(-x, bottom, -z),
                new Vector3(x, bottom, -z),
                new Vector3(x, bottom, z),
                new Vector3(-x, bottom, z),
                new Vector3(-x, bottom, -z),
                new Vector3(-x, top, -z),
                new Vector3(x, top, -z),
                new Vector3(x, bottom, -z),
                new Vector3(x, top, -z),
                new Vector3(x, top, z),
                new Vector3(x, bottom, z),
                new Vector3(x, top, z),
                new Vector3(-x, top, z),
                new Vector3(-x, bottom, z),
                new Vector3(-x, top, z),
                new Vector3(-x, top, -z),
            });
            frame.transform.position = VehiclePosition(pose, false, false);
            frame.transform.rotation = VehicleRotation(pose);
            SetTrackingFrameColor(
                frame,
                new Color(1f, 0.48f, 0.04f));
            return frame;
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
            _robotViews = new GameObject[_plan.RobotTimelines.Length];
            _robotControlMarkers =
                new GameObject[_plan.RobotTimelines.Length];
            _robotControlLabels =
                new TextMesh[_plan.RobotTimelines.Length];
            _robotServiceIndicators = new GameObject[_plan.RobotTimelines.Length];
            _robotLiftVisuals =
                new TransportLiftVisual[_plan.RobotTimelines.Length];
            _robotUsesCustomView = new bool[_plan.RobotTimelines.Length];
            _transportCameras = new Camera[_plan.RobotTimelines.Length];
            _transportCameraFocusOffsets =
                new Vector3[_plan.RobotTimelines.Length];
            _transportCameraYaws = new float[_plan.RobotTimelines.Length];
            _transportCameraPitches = new float[_plan.RobotTimelines.Length];
            _transportCameraDistances = new float[_plan.RobotTimelines.Length];
            for (int robot = 0; robot < _plan.RobotTimelines.Length; robot++)
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
                GameObject deck = CreateChildPrimitive(
                    PrimitiveType.Cube,
                    module.transform,
                    moduleName + "-LiftDeck",
                    new Vector3(0f, 0.061f, 0f),
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
                        // 휠 클램핑 부채: 휴지 시 바퀴 중심 기준 앞뒤 ±55° V자로
                        // 펴져 있고, 리프트 시 부채 접듯 오므라들어 두 팔이
                        // 타이어 앞뒤 접지면 밑 횡방향 평행으로 모인다.
                        float liftYaw = side > 0f ? -90f : 90f;
                        float restYaw = liftYaw +
                            (pairIndex == 0 ? -1f : 1f) * side * 55f;
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
