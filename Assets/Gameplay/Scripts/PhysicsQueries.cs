using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Физика в бою нужна только для лучей (попадания, линия огня, выбор под пальцем): тел с Rigidbody
    /// нет, симулировать нечего. Поэтому шаг симуляции отключён — иначе главный поток каждый кадр ждёт
    /// задачи PhysX, а рабочие потоки бывают надолго заняты пересборкой NavMesh (просадки до 100 мс).
    /// Коллайдеры двигаются вместе с объектами, их положения раз за кадр переносятся в сцену физики —
    /// раньше всех скриптов, которые бросают лучи.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class PhysicsQueries : MonoBehaviour
    {
        SimulationMode previousMode;

        void OnEnable()
        {
            previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        // Режим — настройка проекта: вернуть, чтобы он не остался в редакторе после Play mode
        void OnDisable()
        {
            Physics.simulationMode = previousMode;
        }

        void Update()
        {
            Physics.SyncTransforms();
        }
    }
}
