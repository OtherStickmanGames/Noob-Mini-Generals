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

        /// <summary>
        /// Спрашивается при нажатии: если вернуть true, это нажатие тащит объект (например, здание
        /// при установке), камера не сдвигается, а движение приходит в ObjectDragged / ObjectDragEnded.
        /// </summary>
        public Func<Vector2, bool> TryBeginObjectDrag;
        public event Action<Vector2> ObjectDragged;
        public event Action<Vector2> ObjectDragEnded;

        public Camera Camera { get; private set; }

        Vector3 pivot;
        float yaw;
        float distance = 45f;

        bool pressActive;
        bool dragging;
        bool objectDrag;
        Vector3 pressPosition;
        Vector3 lastPointer;
        float lastPinch;

        // Плавный перевод камеры к точке (GlideTo); прерывается, если игрок сам двигает камеру
        bool gliding;
        Vector3 glideTarget;
        Vector3 glideVelocity;

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

        /// <summary>Плавно перевести камеру так, чтобы точка оказалась в центре экрана</summary>
        public void GlideTo(Vector3 point)
        {
            glideTarget = point;
            glideVelocity = Vector3.zero;
            gliding = true;
        }

        void LateUpdate()
        {
            if (gliding)
            {
                pivot = Vector3.SmoothDamp(pivot, glideTarget, ref glideVelocity, 0.25f);
                ClampPivot();
                if ((pivot - glideTarget).sqrMagnitude < 0.0025f)
                    gliding = false;
            }

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
                objectDrag = pressActive && TryBeginObjectDrag != null && TryBeginObjectDrag(pointer);
                pressPosition = pointer;
                lastPointer = pointer;
            }

            if (pressActive && Input.GetMouseButton(0))
            {
                if (!dragging && (pointer - pressPosition).magnitude > dragThresholdPixels)
                    dragging = true;
                if (dragging && objectDrag)
                    ObjectDragged?.Invoke(pointer);
                else if (dragging)
                    Pan(pointer - lastPointer);
                lastPointer = pointer;
            }

            if (pressActive && Input.GetMouseButtonUp(0))
            {
                if (objectDrag && dragging)
                    ObjectDragEnded?.Invoke(pointer);
                else if (!dragging)
                    Tapped?.Invoke(pointer);
                pressActive = false;
                objectDrag = false;
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

            gliding = false;
            GetFlatAxes(out var right, out var forward);
            pivot += (right * move.x + forward * move.y) * (distance * 0.8f * Time.deltaTime);
            ClampPivot();
        }

        void Pan(Vector3 screenDelta)
        {
            GetFlatAxes(out var right, out var forward);
            gliding = false;
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
