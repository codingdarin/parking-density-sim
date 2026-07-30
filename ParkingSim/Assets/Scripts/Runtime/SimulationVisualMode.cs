using UnityEngine;

namespace ParkingSim.Runtime
{
    public enum SimulationVisualMode
    {
        Control,
        ThreeDimensional
    }

    public enum SimulationVisualLayer
    {
        Shared,
        Control,
        ThreeDimensional
    }

    /// <summary>
    /// Keeps simulation actors independent from mode-specific presentation.
    /// Imported models can replace objects inside a layer without changing Core state.
    /// </summary>
    public sealed class SimulationVisualLayers
    {
        public GameObject Root { get; private set; }
        public GameObject SharedRoot { get; private set; }
        public GameObject ControlRoot { get; private set; }
        public GameObject ThreeDimensionalRoot { get; private set; }

        public SimulationVisualLayers()
        {
            Root = new GameObject("ModelV2-VisualRoot");
            SharedRoot = CreateRoot("Shared-SimulationActors");
            ControlRoot = CreateRoot("ControlMode-Overlays");
            ThreeDimensionalRoot = CreateRoot("ThreeDimensionalMode-Environment");
        }

        public void Track(GameObject target, SimulationVisualLayer layer)
        {
            Transform parent = SharedRoot.transform;
            if (layer == SimulationVisualLayer.Control)
                parent = ControlRoot.transform;
            else if (layer == SimulationVisualLayer.ThreeDimensional)
                parent = ThreeDimensionalRoot.transform;
            target.transform.SetParent(parent, worldPositionStays: true);
        }

        public void SetMode(SimulationVisualMode mode)
        {
            SharedRoot.SetActive(true);
            ControlRoot.SetActive(mode == SimulationVisualMode.Control);
            ThreeDimensionalRoot.SetActive(
                mode == SimulationVisualMode.ThreeDimensional);
        }

        private GameObject CreateRoot(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(Root.transform, worldPositionStays: false);
            return child;
        }
    }

    public sealed class SimulationCameraController
    {
        private Camera _camera;

        public Camera Apply(
            SimulationVisualMode mode,
            int width,
            int height,
            Camera preferredCamera)
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>();
            Camera camera = preferredCamera != null
                ? preferredCamera
                : cameras.Length > 0 ? cameras[0] : null;
            for (int i = 0; i < cameras.Length; i++)
                if (cameras[i] != camera) cameras[i].gameObject.SetActive(false);
            if (camera == null)
            {
                var cameraObject = new GameObject("ModelV2-Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.gameObject.SetActive(true);
            float aspect = camera.aspect > 0.01f ? camera.aspect : 16f / 9f;
            float centerX = (width - 1) / 2f;
            float centerZ = (height - 1) / 2f;
            float size = Mathf.Max(
                height / 2f,
                width / (2f * aspect)) + 2.5f;
            bool control = mode == SimulationVisualMode.Control;
            camera.orthographic = control;
            camera.orthographicSize = size;
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.04f, 0.07f);
            if (control)
            {
                camera.transform.position = new Vector3(centerX, 35f, centerZ);
                camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                float distance = Mathf.Max(width, height) * 1.15f;
                Quaternion orbit = Quaternion.Euler(42f, 0f, 0f);
                Vector3 forward = orbit * Vector3.forward;
                camera.transform.position =
                    new Vector3(centerX, 0f, centerZ) - forward * distance;
                camera.transform.LookAt(new Vector3(centerX, 0f, centerZ));
            }

            _camera = camera;
            return _camera;
        }
    }
}
