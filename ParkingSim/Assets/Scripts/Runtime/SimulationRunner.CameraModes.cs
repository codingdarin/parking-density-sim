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
        /// <summary>
        /// 추적 카메라와 유닛 사이를 가리는 건물을 유리 실루엣으로 전환한다.
        /// 경계 상자-시선 레이 검사(콜라이더 불요) — 벗어나면 원 재질 복원.
        /// </summary>
        private void UpdateTrackingOcclusion()
        {
            bool tracking = _selectedTransportCamera >= 0 &&
                _transportCameras != null &&
                _robotViews != null &&
                _selectedTransportCamera < _transportCameras.Length &&
                _selectedTransportCamera < _robotViews.Length &&
                _transportCameras[_selectedTransportCamera] != null &&
                _robotViews[_selectedTransportCamera] != null;
            Vector3 cameraPosition = Vector3.zero;
            Vector3 unitPosition = Vector3.zero;
            float distance = 0f;
            if (tracking)
            {
                cameraPosition = _transportCameras[_selectedTransportCamera]
                    .transform.position;
                unitPosition = _robotViews[_selectedTransportCamera]
                    .transform.position + Vector3.up * 0.2f;
                distance = Vector3.Distance(cameraPosition, unitPosition);
            }
            foreach (OccluderEntry entry in _occluders)
            {
                bool occludes = false;
                if (tracking && distance > 0.01f)
                {
                    var ray = new Ray(
                        cameraPosition,
                        (unitPosition - cameraPosition) / distance);
                    occludes = entry.Bounds.IntersectRay(ray, out float hit) &&
                               hit < distance;
                }
                if (occludes == entry.Ghosted) continue;
                entry.Ghosted = occludes;
                for (int index = 0; index < entry.Renderers.Length; index++)
                {
                    Renderer renderer = entry.Renderers[index];
                    if (renderer == null) continue;
                    if (occludes)
                    {
                        Material ghost = GhostMaterial();
                        var materials =
                            new Material[renderer.sharedMaterials.Length];
                        for (int slot = 0; slot < materials.Length; slot++)
                            materials[slot] = ghost;
                        renderer.sharedMaterials = materials;
                    }
                    else
                    {
                        renderer.sharedMaterials = entry.Originals[index];
                    }
                }
            }
        }

        private Material GhostMaterial()
        {
            if (_ghostMaterial != null) return _ghostMaterial;
            _ghostMaterial = CreatePlumbobGlassMaterial();
            _ghostMaterial.name = "OccluderGhost";
            // 가림 유리는 플럼밥보다 훨씬 투명하게 — 광택을 낮춰 흰 번들거림 억제
            Color tint = new Color(0.55f, 0.62f, 0.70f, 0.12f);
            _ghostMaterial.color = tint;
            if (_ghostMaterial.HasProperty("_BaseColor"))
                _ghostMaterial.SetColor("_BaseColor", tint);
            if (_ghostMaterial.HasProperty("_Smoothness"))
                _ghostMaterial.SetFloat("_Smoothness", 0.55f);
            if (_ghostMaterial.HasProperty("_Metallic"))
                _ghostMaterial.SetFloat("_Metallic", 0f);
            return _ghostMaterial;
        }

        private void SetupLighting()
        {
            Light[] lights = Object.FindObjectsByType<Light>();
            for (int i = 0; i < lights.Length; i++) lights[i].gameObject.SetActive(false);
            var lightObject = new GameObject("ModelV2-KeyLight");
            Track(lightObject, SimulationVisualLayer.Shared);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.94f, 0.84f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.ambientLight = new Color(0.28f, 0.32f, 0.40f);
        }

        private void ApplyVisualMode()
        {
            if (_visualLayers == null || _problem == null) return;
            _selectedTransportCamera = -1;
            _visualLayers.SetMode(_visualMode);
            _presentationCamera = _cameraController.Apply(
                _visualMode,
                _problem.Width,
                _problem.Height,
                _presentationCamera);
            _presentationCamera.enabled = true;
            if (_visualMode == SimulationVisualMode.ThreeDimensional)
            {
                EnsurePresentationCameraNavigation();
                ApplyPresentationCameraPose();
            }
            Debug.Log(
                "[Model V2] camera=" + _presentationCamera.name +
                (_visualMode == SimulationVisualMode.Control
                    ? ", control"
                    : ", three-dimensional"));
        }

        private void SelectTransportCamera(int cameraIndex)
        {
            if (_transportCameras == null ||
                cameraIndex < 0 ||
                cameraIndex >= _transportCameras.Length)
            {
                _inputStatus =
                    "운송유닛 " + (cameraIndex + 1) + " 카메라를 사용할 수 없음";
                return;
            }
            _visualMode = SimulationVisualMode.ThreeDimensional;
            if (_visualLayers != null) _visualLayers.SetMode(_visualMode);
            if (_presentationCamera != null)
                _presentationCamera.enabled = false;
            for (int index = 0; index < _transportCameras.Length; index++)
            {
                Camera camera = _transportCameras[index];
                if (camera == null) continue;
                bool selected = index == cameraIndex;
                camera.gameObject.SetActive(selected);
                camera.enabled = selected;
            }
            _selectedTransportCamera = cameraIndex;
            ApplyTransportCameraPose(cameraIndex);
            _inputStatus =
                "운송유닛 " + (cameraIndex + 1) +
                " 추적 카메라 · 관제/3D 버튼으로 전체 화면 복귀";
        }

        private void UpdateCameraNavigation()
        {
            bool tracking = _selectedTransportCamera >= 0;
            if (!tracking &&
                _visualMode != SimulationVisualMode.ThreeDimensional)
                return;

            Vector2 move;
            Vector2 orbit;
            float zoom;
            bool fast;
            ReadCameraNavigationInput(out move, out orbit, out zoom, out fast);
            float deltaTime = Time.unscaledDeltaTime;
            if (tracking)
            {
                int index = _selectedTransportCamera;
                if (_transportCameras == null ||
                    index >= _transportCameras.Length)
                    return;
                float distance = _transportCameraDistances[index];
                float speed =
                    Mathf.Max(0.55f, distance * 0.62f) *
                    deltaTime * (fast ? 2.5f : 1f);
                Quaternion heading = Quaternion.Euler(
                    0f, _transportCameraYaws[index], 0f);
                Vector3 offset = _transportCameraFocusOffsets[index];
                offset +=
                    (heading * Vector3.right * move.x +
                     heading * Vector3.forward * move.y) * speed;
                offset.x = Mathf.Clamp(offset.x, -5f, 5f);
                offset.z = Mathf.Clamp(offset.z, -5f, 5f);
                _transportCameraFocusOffsets[index] = offset;
                _transportCameraYaws[index] += orbit.x;
                _transportCameraPitches[index] = Mathf.Clamp(
                    _transportCameraPitches[index] - orbit.y,
                    8f,
                    78f);
                _transportCameraDistances[index] = Mathf.Clamp(
                    distance * Mathf.Exp(
                        -zoom * CameraWheelZoomExponent),
                    0.85f,
                    8f);
                ApplyTransportCameraPose(index);
                return;
            }

            EnsurePresentationCameraNavigation();
            float presentationSpeed =
                Mathf.Max(3f, _presentationCameraDistance * 0.34f) *
                deltaTime * (fast ? 2.5f : 1f);
            Quaternion presentationHeading =
                Quaternion.Euler(0f, _presentationCameraYaw, 0f);
            _presentationCameraFocus +=
                (presentationHeading * Vector3.right * move.x +
                 presentationHeading * Vector3.forward * move.y) *
                presentationSpeed;
            _presentationCameraYaw += orbit.x;
            _presentationCameraPitch = Mathf.Clamp(
                _presentationCameraPitch - orbit.y,
                12f,
                78f);
            _presentationCameraDistance = Mathf.Clamp(
                _presentationCameraDistance * Mathf.Exp(
                    -zoom * CameraWheelZoomExponent),
                12f,
                110f);
            ApplyPresentationCameraPose();
        }

        private void ApplyThreeDimensionalLabelFacing()
        {
            if (_visualMode != SimulationVisualMode.ThreeDimensional)
                return;
            Camera camera = ActiveViewCamera();
            if (camera == null) return;
            Quaternion screenFacingRotation = camera.transform.rotation;
            for (int index = 0;
                 index < _threeDimensionalLabels.Count;
                 index++)
            {
                TextMesh label = _threeDimensionalLabels[index];
                if (label != null)
                    label.transform.rotation = screenFacingRotation;
            }
        }

        private void EnsurePresentationCameraNavigation()
        {
            if (_presentationCameraNavigationInitialized || _problem == null)
                return;
            _presentationCameraFocus = new Vector3(
                (_problem.Width - 1) / 2f,
                0f,
                (_problem.Height - 1) / 2f);
            _presentationCameraYaw = 0f;
            _presentationCameraPitch = 42f;
            _presentationCameraDistance =
                Mathf.Max(_problem.Width, _problem.Height) * 1.15f;
            _presentationCameraNavigationInitialized = true;
        }

        private void ApplyPresentationCameraPose()
        {
            if (_presentationCamera == null) return;
            Quaternion orbit = Quaternion.Euler(
                _presentationCameraPitch,
                _presentationCameraYaw,
                0f);
            Vector3 forward = orbit * Vector3.forward;
            _presentationCamera.transform.position =
                _presentationCameraFocus -
                forward * _presentationCameraDistance;
            _presentationCamera.transform.rotation =
                Quaternion.LookRotation(forward, Vector3.up);
        }

        private void ApplyTransportCameraPose(int index)
        {
            if (_transportCameras == null ||
                _transportCameraFocusOffsets == null ||
                index < 0 ||
                index >= _transportCameras.Length ||
                _transportCameras[index] == null)
                return;
            Quaternion orbit = Quaternion.Euler(
                _transportCameraPitches[index],
                _transportCameraYaws[index],
                0f);
            Vector3 forward = orbit * Vector3.forward;
            Transform cameraTransform = _transportCameras[index].transform;
            Transform transport = cameraTransform.parent;
            Vector3 transportPosition = transport != null
                ? transport.position
                : Vector3.zero;
            Vector3 worldFocus =
                transportPosition + _transportCameraFocusOffsets[index];
            cameraTransform.position =
                worldFocus - forward * _transportCameraDistances[index];
            cameraTransform.rotation =
                Quaternion.LookRotation(forward, Vector3.up);
        }

        private static void ReadCameraNavigationInput(
            out Vector2 move,
            out Vector2 orbit,
            out float zoom,
            out bool fast)
        {
            move = Vector2.zero;
            orbit = Vector2.zero;
            zoom = 0f;
            fast = false;
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                move.x =
                    (keyboard.dKey.isPressed ? 1f : 0f) -
                    (keyboard.aKey.isPressed ? 1f : 0f);
                move.y =
                    (keyboard.wKey.isPressed ? 1f : 0f) -
                    (keyboard.sKey.isPressed ? 1f : 0f);
                fast =
                    keyboard.leftShiftKey.isPressed ||
                    keyboard.rightShiftKey.isPressed;
            }
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                    orbit = mouse.delta.ReadValue() * 0.16f;
                zoom = mouse.scroll.ReadValue().y / 120f;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            move.x =
                (Input.GetKey(KeyCode.D) ? 1f : 0f) -
                (Input.GetKey(KeyCode.A) ? 1f : 0f);
            move.y =
                (Input.GetKey(KeyCode.W) ? 1f : 0f) -
                (Input.GetKey(KeyCode.S) ? 1f : 0f);
            fast =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);
            if (Input.GetMouseButton(1))
                orbit = new Vector2(
                    Input.GetAxis("Mouse X") * 3.2f,
                    Input.GetAxis("Mouse Y") * 3.2f);
            zoom = Input.GetAxis("Mouse ScrollWheel") * 10f;
#endif
            if (move.sqrMagnitude > 1f) move.Normalize();
        }

        private void AnimateFireMarker()
        {
            if (_fireMarker == null) return;
            if (_fireUsesCustomView) return;
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f);
            _fireMarker.transform.localScale = new Vector3(
                0.34f * pulse,
                0.14f * (1f + 0.08f * Mathf.Sin(Time.unscaledTime * 7f)),
                0.34f * pulse);
        }

    }
}
