using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Generals
{
    /// <summary>
    /// Камера боя: своя база внизу экрана, сдвиг пальцем или мышью, зум двумя пальцами или колесом.
    /// Короткое касание без сдвига — событие Tapped (для выбора места и зданий).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class RtsCamera : MonoBehaviour
    {
        [SerializeField] VoxelArena arena;
        [SerializeField] float pitch = 55f;
        [SerializeField] float minDistance = 10f;
        [SerializeField] float maxDistance = 90f;
        [SerializeField] float dragThresholdPixels = 12f;

        /// <summary>Касание по миру (не по интерфейсу) в экранных координатах</summary>
        public event Action<Vector2> Tapped;

        public Camera Camera { get; private set; }

        Vector3 pivot;
        float yaw;
        float distance = 45f;

        bool pressActive;
        bool dragging;
        Vector3 pressPosition;
        Vector3 lastPointer;
        float lastPinch;

        void Awake()
        {
            Camera = GetComponent<Camera>();
            arena.Generated += FocusOwnBase;
        }

        void Start()
        {
            Camera.farClipPlane = Mathf.Max(Camera.farClipPlane, 600f);
            if (arena.Layout != null)
                FocusOwnBase();
        }

        void OnDestroy()
        {
            arena.Generated -= FocusOwnBase;
        }

        void FocusOwnBase()
        {
            var axis = arena.Layout.baseAxis;
            yaw = Mathf.Atan2(axis.x, axis.y) * Mathf.Rad2Deg;
            pivot = arena.BaseOne + new Vector3(axis.x, 0f, axis.y) * 12f;
            distance = 45f;

            var color = ArenaBiomes.FogColor(arena.Biome);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = color;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = color;
        }

        void Update()
        {
            HandlePinch();
            HandlePointer();
            HandleKeys();
        }

        void LateUpdate()
        {
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);

            RenderSettings.fogStartDistance = distance * 1.2f;
            RenderSettings.fogEndDistance = distance * 2.8f;
        }

        void HandlePointer()
        {
            if (Input.touchCount > 1)
            {
                dragging = true;
                return;
            }

            Vector3 pointer = Input.mousePosition;

            if (Input.GetMouseButtonDown(0))
            {
                pressActive = !PointerOverUI();
                dragging = false;
                pressPosition = pointer;
                lastPointer = pointer;
            }

            if (pressActive && Input.GetMouseButton(0))
            {
                if (!dragging && (pointer - pressPosition).magnitude > dragThresholdPixels)
                    dragging = true;
                if (dragging)
                    Pan(pointer - lastPointer);
                lastPointer = pointer;
            }

            if (pressActive && Input.GetMouseButtonUp(0))
            {
                if (!dragging)
                    Tapped?.Invoke(pointer);
                pressActive = false;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f && !PointerOverUI())
                distance = Mathf.Clamp(distance * (1f - scroll * 0.1f), minDistance, maxDistance);
        }

        static bool PointerOverUI()
        {
            var system = EventSystem.current;
            if (system == null)
                return false;
            if (Input.touchCount > 0)
                return system.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            return system.IsPointerOverGameObject();
        }

        void HandlePinch()
        {
            if (Input.touchCount != 2)
            {
                lastPinch = 0f;
                return;
            }

            float pinch = (Input.GetTouch(0).position - Input.GetTouch(1).position).magnitude;
            if (lastPinch > 0f && pinch > 0f)
                distance = Mathf.Clamp(distance * lastPinch / pinch, minDistance, maxDistance);
            lastPinch = pinch;
        }

        void HandleKeys()
        {
            var move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (move == Vector2.zero)
                return;

            GetFlatAxes(out var right, out var forward);
            pivot += (right * move.x + forward * move.y) * (distance * 0.8f * Time.deltaTime);
            ClampPivot();
        }

        void Pan(Vector3 screenDelta)
        {
            GetFlatAxes(out var right, out var forward);
            float scale = distance / Screen.height * 1.5f;
            pivot -= (right * screenDelta.x + forward * screenDelta.y) * scale;
            ClampPivot();
        }

        void GetFlatAxes(out Vector3 right, out Vector3 forward)
        {
            right = transform.right;
            right.y = 0f;
            right.Normalize();
            forward = Vector3.Cross(right, Vector3.up);
        }

        void ClampPivot()
        {
            var bounds = arena.PlayableBounds;
            pivot.x = Mathf.Clamp(pivot.x, bounds.min.x, bounds.max.x);
            pivot.z = Mathf.Clamp(pivot.z, bounds.min.z, bounds.max.z);
        }
    }
}
