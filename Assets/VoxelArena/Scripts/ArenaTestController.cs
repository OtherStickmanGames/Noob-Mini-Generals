using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Тестовое управление ареной: камера, взрывы, агенты, замеры на экране.
/// Мышь: ЛКМ клик — взрыв, ЛКМ тянуть — сдвиг камеры, ПКМ — отправить агентов, колесо — зум.
/// Клавиши: WASD — сдвиг, Q/E — поворот, G — новая карта.
/// Телефон: тап — взрыв, палец тянуть — сдвиг, два пальца — зум.
/// </summary>
[RequireComponent(typeof(VoxelArena), typeof(ArenaNavMesh))]
public class ArenaTestController : MonoBehaviour
{
    [SerializeField] float explosionRadius = 2.5f;
    [SerializeField] int agentCount = 6;
    [SerializeField] float pitch = 55f;
    [SerializeField] float yaw = 0f;
    [SerializeField] float dragThresholdPixels = 12f;

    VoxelArena arena;
    ArenaNavMesh navMesh;
    Camera cam;

    Vector3 pivot;
    float distance;
    float minDistance;
    float maxDistance;

    readonly List<NavMeshAgent> agents = new();
    bool agentsGoToTwo = true;

    bool pressActive;
    bool dragging;
    Vector3 pressPosition;
    Vector3 lastPointer;
    float lastPinch;

    float fps;
    int lastRemoved;
    string lastClick = "—";
    Rect panelRect;

    void Start()
    {
        arena = GetComponent<VoxelArena>();
        navMesh = GetComponent<ArenaNavMesh>();
        cam = Camera.main;

        var bounds = arena.WorldBounds;
        pivot = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.3f, bounds.center.z);
        maxDistance = Mathf.Max(bounds.size.x, bounds.size.z) * 1.4f;
        minDistance = 4f;
        distance = maxDistance * 0.7f;
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, maxDistance * 3f);
    }

    void Update()
    {
        fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), 0.05f);

        HandlePinch();
        HandlePointer();
        HandleKeys();

        if (agents.Count == 0 && navMesh.HasNavMesh && !navMesh.IsBusy)
            SpawnAgents();
    }

    void LateUpdate()
    {
        var rotation = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
    }

    void HandlePointer()
    {
        if (Input.touchCount > 1)
        {
            // Два пальца — это зум, тап не засчитываем
            dragging = true;
            return;
        }

        Vector3 pointer = Input.mousePosition;

        if (Input.GetMouseButtonDown(0))
        {
            pressActive = !InPanel(pointer);
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
                ExplodeAt(pointer);
            else
                lastClick = "перетаскивание камеры, не взрыв";

            pressActive = false;
        }

        if (Input.GetMouseButtonDown(1) && RaycastArena(pointer, out var hit))
            SendAgents(hit.point);

        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
            distance = Mathf.Clamp(distance * (1f - scroll * 0.1f), minDistance, maxDistance);
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
        if (move != Vector2.zero)
        {
            GetFlatAxes(out var right, out var forward);
            pivot += (right * move.x + forward * move.y) * (distance * 0.8f * Time.deltaTime);
            ClampPivot();
        }

        if (Input.GetKey(KeyCode.Q)) yaw += 90f * Time.deltaTime;
        if (Input.GetKey(KeyCode.E)) yaw -= 90f * Time.deltaTime;

        if (Input.GetKeyDown(KeyCode.G))
            Regenerate();
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
        right = cam.transform.right;
        right.y = 0f;
        right.Normalize();
        forward = Vector3.Cross(right, Vector3.up);
    }

    void ClampPivot()
    {
        var bounds = arena.WorldBounds;
        pivot.x = Mathf.Clamp(pivot.x, bounds.min.x, bounds.max.x);
        pivot.z = Mathf.Clamp(pivot.z, bounds.min.z, bounds.max.z);
    }

    bool RaycastArena(Vector3 screenPoint, out RaycastHit hit)
    {
        var ray = cam.ScreenPointToRay(screenPoint);
        return Physics.Raycast(ray, out hit, 1000f) && hit.collider.transform.IsChildOf(arena.transform);
    }

    void ExplodeAt(Vector3 screenPoint)
    {
        if (!RaycastArena(screenPoint, out var hit))
        {
            lastClick = "мимо арены";
            return;
        }

        // Центр чуть внутри поверхности, чтобы воронка была и вглубь
        var center = hit.point - hit.normal * (arena.VoxelSize * 0.5f);
        lastRemoved = arena.Explode(center, explosionRadius);
        lastClick = hit.collider.name;
    }

    void Regenerate()
    {
        foreach (var agent in agents)
            Destroy(agent.gameObject);
        agents.Clear();

        arena.Seed = Random.Range(1, 100000);
        arena.Generate();
    }

    void SpawnAgents()
    {
        for (int i = 0; i < agentCount; i++)
        {
            var offset = Random.insideUnitCircle * 3f;
            var point = arena.BaseOne + new Vector3(offset.x, 0f, offset.y);

            if (!NavMesh.SamplePosition(point, out var navHit, 4f, NavMesh.AllAreas))
                continue;

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"Test Agent {i}";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = navHit.position;

            var agent = go.AddComponent<NavMeshAgent>();
            agent.agentTypeID = navMesh.AgentTypeID;
            agent.speed = 4f;
            agent.SetDestination(agentsGoToTwo ? arena.BaseTwo : arena.BaseOne);
            agents.Add(agent);
        }
    }

    void SendAgents(Vector3 target)
    {
        foreach (var agent in agents)
            agent.SetDestination(target);
    }

    void ToggleAgentsTarget()
    {
        agentsGoToTwo = !agentsGoToTwo;
        SendAgents(agentsGoToTwo ? arena.BaseTwo : arena.BaseOne);
    }

    bool InPanel(Vector3 screenPoint)
    {
        return panelRect.Contains(new Vector2(screenPoint.x, Screen.height - screenPoint.y));
    }

    void OnGUI()
    {
        int fontSize = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 55, 10, 28);
        GUI.skin.label.fontSize = fontSize;
        GUI.skin.button.fontSize = fontSize;
        GUI.skin.label.wordWrap = false;

        // Высота панели считается по содержимому, чтобы кнопки не обрезались
        var area = new Rect(10, 10, Screen.width - 20, Screen.height - 20);
        GUILayout.BeginArea(area);
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(fontSize * 22));

        var dims = arena.Dims;
        GUILayout.Label($"FPS: {fps:0}");
        GUILayout.Label($"Арена {dims.x}x{dims.y}x{dims.z}, воксель {arena.VoxelSize}");
        GUILayout.Label($"Треугольников: {arena.TotalTriangles}");
        GUILayout.Label($"Перестроено чанков: {arena.LastRebuiltChunks}, выбито: {lastRemoved}");
        GUILayout.Label($"  меш (джобы): {arena.LastMeshMs:0.00} мс");
        GUILayout.Label($"  меш + коллайдер: {arena.LastApplyMs:0.00} мс");
        GUILayout.Label($"NavMesh: {navMesh.LastBuildMs:0.0} мс за {navMesh.LastBuildFrames} кадр., плитка {navMesh.TileSize}");
        GUILayout.Label($"Клик: {lastClick}");

        if (GUILayout.Button("Новая карта"))
            Regenerate();

        if (GUILayout.Button("Агенты к другой базе"))
            ToggleAgentsTarget();

        GUILayout.EndVertical();

        if (Event.current.type == EventType.Repaint)
        {
            var panel = GUILayoutUtility.GetLastRect();
            panelRect = new Rect(panel.x + area.x, panel.y + area.y, panel.width, panel.height);
        }

        GUILayout.EndArea();
    }
}
