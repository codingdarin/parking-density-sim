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
        private Color CellColor(int x, int y)
        {
            if (!_problem.IsFloor(x, y)) return new Color(0.09f, 0.10f, 0.12f);
            if (IsSlotCell(x, y, SlotKind.Staging)) return new Color(0.12f, 0.42f, 0.24f);
            if (IsSlotCell(x, y, SlotKind.Blocking)) return new Color(0.48f, 0.16f, 0.16f);
            if (_problem.IsClearanceCell(x, y)) return new Color(0.42f, 0.30f, 0.10f);
            return new Color(0.22f, 0.25f, 0.30f);
        }

        private bool IsSlotCell(int x, int y, SlotKind kind)
        {
            foreach (ParkingSlotV2 slot in _problem.Slots)
            {
                if (slot.Kind != kind) continue;
                var second = slot.Pose.SecondCell;
                if ((slot.Pose.X == x && slot.Pose.Y == y) ||
                    (second.X == x && second.Y == y)) return true;
            }
            return false;
        }

        private static TimedRobotStateV2 StateAt(List<TimedRobotStateV2> timeline, int tick)
        {
            for (int i = timeline.Count - 1; i >= 0; i--)
                if (timeline[i].Tick <= tick) return timeline[i];
            return timeline[0];
        }

        private static Vector3 RobotPosition(
            TimedRobotStateV2 robot,
            bool customTransport)
        {
            float x = robot.X;
            float z = robot.Y;
            if (robot.Carrying)
            {
                VehiclePose pose = new VehiclePose(
                    robot.X,
                    robot.Y,
                    robot.Orientation);
                var second = pose.SecondCell;
                x = (pose.X + second.X) * 0.5f;
                z = (pose.Y + second.Y) * 0.5f;
            }
            return new Vector3(x, customTransport ? 0f : 0.20f, z);
        }

        private static Vector3 RobotPosition(
            VehiclePose pose,
            bool customTransport)
        {
            var second = pose.SecondCell;
            return new Vector3(
                (pose.X + second.X) * 0.5f,
                customTransport ? 0f : 0.20f,
                (pose.Y + second.Y) * 0.5f);
        }

        private static Quaternion RobotVisualTargetRotation(
            TimedRobotStateV2 from,
            TimedRobotStateV2 to,
            float fraction,
            Quaternion current)
        {
            if (from.Carrying || to.Carrying)
                return Quaternion.Lerp(
                    RobotRotation(from),
                    RobotRotation(to),
                    fraction);

            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            if (dx != 0)
                return Quaternion.identity;
            if (dy != 0)
                return Quaternion.Euler(0f, 90f, 0f);
            return current;
        }

        private static Quaternion SmoothRobotRotation(
            Quaternion current,
            Quaternion target)
        {
            return Quaternion.RotateTowards(
                current,
                target,
                300f * Time.unscaledDeltaTime);
        }

        private static Quaternion RobotRotation(TimedRobotStateV2 robot)
        {
            return robot.Orientation == VehicleOrientation.Horizontal
                ? Quaternion.identity
                : Quaternion.Euler(0f, 90f, 0f);
        }

        private static Vector3 VehiclePosition(
            VehiclePose pose,
            bool carried,
            bool customTransport)
        {
            var second = pose.SecondCell;
            float height = carried
                ? customTransport ? 0.44f : 0.52f
                : ParkedVehicleRootHeight;
            return new Vector3(
                (pose.X + second.X) / 2f,
                height,
                (pose.Y + second.Y) / 2f);
        }

        private static Quaternion VehicleRotation(VehiclePose pose)
        {
            return pose.Orientation == VehicleOrientation.Horizontal
                ? Quaternion.identity
                : Quaternion.Euler(0f, 90f, 0f);
        }

        private static Color VehicleBodyColor(int vehicle)
        {
            switch (Mathf.Abs(vehicle) % 10)
            {
                case 0:
                case 4:
                    return new Color(0.86f, 0.87f, 0.88f);
                case 1:
                case 6:
                    return new Color(0.055f, 0.060f, 0.070f);
                case 2:
                case 5:
                    return new Color(0.34f, 0.36f, 0.39f);
                case 3:
                    return new Color(0.12f, 0.13f, 0.15f);
                case 7:
                    return new Color(0.60f, 0.63f, 0.66f);
                case 8:
                    return new Color(0.72f, 0.08f, 0.055f);
                default:
                    return new Color(0.055f, 0.20f, 0.60f);
            }
        }

        private static Color RobotColor(int robot, bool carrying)
        {
            if (carrying) return new Color(1f, 0.48f, 0.05f);
            return Color.HSVToRGB((robot * 0.137f + 0.52f) % 1f, 0.78f, 0.95f);
        }

        private static void SetColor(GameObject target, Color color)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            Material material = renderer.material;
            Shader urpLit =
                Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit != null && material.shader != urpLit)
            {
                material = new Material(urpLit)
                {
                    name = target.name + "-RuntimeLit"
                };
                renderer.material = material;
            }
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }

        private static void SetCarBodyColor(GameObject car, Color color)
        {
            Renderer[] renderers = car.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Material[] materials = renderers[rendererIndex].materials;
                for (int materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null ||
                        !material.name.StartsWith("Body"))
                        continue;
                    material.color = color;
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", color);
                }
            }
        }

        private static Material CreateTrackingLineMaterial()
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Standard");
            return new Material(shader);
        }

        private static void SetTrackingFrameColor(
            GameObject frame,
            Color color)
        {
            LineRenderer line = frame.GetComponent<LineRenderer>();
            if (line != null)
            {
                line.startColor = color;
                line.endColor = color;
                ApplyMaterialColor(line.material, color);
                return;
            }
            MeshRenderer renderer = frame.GetComponentInChildren<MeshRenderer>();
            if (renderer == null) return;
            ApplyMaterialColor(renderer.material, color);
        }

        private static void ApplyMaterialColor(Material material, Color color)
        {
            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
        }

        private static GameObject CreateChildPrimitive(
            PrimitiveType type,
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            GameObject child = GameObject.CreatePrimitive(type);
            child.name = name;
            child.transform.SetParent(parent, worldPositionStays: false);
            child.transform.localPosition = localPosition;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = localScale;
            SetColor(child, color);
            return child;
        }

        private GameObject CreateSitePrimitive(
            PrimitiveType type,
            string name,
            Vector3 position,
            Vector3 scale,
            Material material,
            Transform parent = null,
            bool localPosition = false)
        {
            GameObject child = GameObject.CreatePrimitive(type);
            child.name = name;
            if (parent == null)
            {
                Track(child, SimulationVisualLayer.ThreeDimensional);
                child.transform.position = position;
            }
            else
            {
                child.transform.SetParent(parent, worldPositionStays: false);
                if (localPosition)
                    child.transform.localPosition = position;
                else
                    child.transform.position = position;
            }
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = scale;
            SetSharedMaterial(child, material);
            Collider collider = child.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return child;
        }

        private static void SetSharedMaterial(
            GameObject target,
            Material material)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        private static void DisableColliders(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }

        private void Track(GameObject target, SimulationVisualLayer layer)
        {
            _visualLayers.Track(target, layer);
        }

    }
}
