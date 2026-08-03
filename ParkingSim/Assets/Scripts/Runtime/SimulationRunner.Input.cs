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
        private static bool TransportCameraKeyPressed(out int cameraIndex)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) cameraIndex = 0;
                else if (keyboard.digit2Key.wasPressedThisFrame) cameraIndex = 1;
                else if (keyboard.digit3Key.wasPressedThisFrame) cameraIndex = 2;
                else if (keyboard.digit4Key.wasPressedThisFrame) cameraIndex = 3;
                else
                {
                    cameraIndex = -1;
                    return false;
                }
                return true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha1)) cameraIndex = 0;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) cameraIndex = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) cameraIndex = 2;
            else if (Input.GetKeyDown(KeyCode.Alpha4)) cameraIndex = 3;
            else
            {
                cameraIndex = -1;
                return false;
            }
            return true;
#endif
            cameraIndex = -1;
            return false;
        }

        private static bool PointerPressed(out Vector2 position)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                position = mouse.position.ReadValue();
                return true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0))
            {
                position = Input.mousePosition;
                return true;
            }
#endif
            position = Vector2.zero;
            return false;
        }

        private static Rect ControlPanelBounds()
        {
            return new Rect(
                Mathf.Max(12f, Screen.width - ControlPanelWidth - 12f),
                12f,
                ControlPanelWidth,
                ControlPanelHeight);
        }

        private static bool IsPointerOverHud(Vector2 screenPosition)
        {
            Vector2 guiPosition = new Vector2(
                screenPosition.x,
                Screen.height - screenPosition.y);
            return GuideBounds.Contains(guiPosition) ||
                   ControlPanelBounds().Contains(guiPosition) ||
                   ReadinessPanelBounds().Contains(guiPosition) ||
                   PlaybackBarBounds().Contains(guiPosition);
        }

        private static bool PauseTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Space);
#else
            return false;
#endif
        }

        /// <summary>` (백틱) — 관제모드/3D모드 토글</summary>
        private static bool ModeTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null &&
                   Keyboard.current.backquoteKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.BackQuote);
#else
            return false;
#endif
        }

        private static bool ReplayPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.R);
#else
            return false;
#endif
        }

        private bool TryResolveClickedBuilding(
            Vector2 screenPosition,
            out int buildingId,
            out string failure)
        {
            Camera camera = ActiveViewCamera();
            if (camera == null) camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                buildingId = 0;
                failure = "활성 카메라가 없어 화재동을 선택할 수 없음";
                return false;
            }
            Ray ray = camera.ScreenPointToRay(screenPosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 100f))
            {
                buildingId = 0;
                failure = "아파트동을 클릭해야 함";
                return false;
            }
            (int X, int Y) cell = (
                Mathf.RoundToInt(hit.point.x),
                Mathf.RoundToInt(hit.point.z));
            ApartmentBuildingV2 building = _complex == null
                ? null
                : _complex.Buildings.FirstOrDefault(candidate =>
                    candidate.FootprintCells.Contains(cell));
            if (building == null)
            {
                buildingId = 0;
                failure =
                    "선택 위치 (" + cell.X + "," + cell.Y +
                    ")에 등록된 아파트동이 없음";
                return false;
            }
            buildingId = building.Id;
            failure = null;
            return true;
        }

    }
}
