using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace Generals
{
    /// <summary>
    /// Автотест боя (запуск — Tools/autotest.sh, редакторная часть — Editor/AutoTestRunner.cs): обе
    /// стороны ведёт скриптовый ИИ, время ускорено. Раз в секунду игрового времени проверяет юнитов:
    /// застрявших (есть путь, но почти не двигаются), атакующих, которые долго не выходят со своей базы,
    /// заводы с перекрытым выездом. Пишет события, ошибки и предупреждения в отчёт, снимает кадры баз и
    /// места боя. В конце закрывает редактор.
    /// </summary>
    public class AutoTestDriver : MonoBehaviour
    {
        public float durationSeconds = 600f;
        public float timeScale = 3f;
        public float screenshotInterval = 45f;
        public string outputDir = "AutoTest";
        /// <summary>Базового ресурса в секунду каждой стороне сверх обычного — чтобы быстрее дойти до
        /// поздних построек (завод, техника)</summary>
        public float bonusIncome;

        // Застрял: есть путь дальше этого, а за StuckWindow секунд сдвинулся меньше StuckMove метров
        const float StuckWindow = 8f;
        const float StuckMove = 0.6f;
        // Атакующий дольше этого внутри своих стен — «не вышел в атаку»
        const float StayHomeLimit = 45f;

        readonly StringBuilder report = new();
        readonly Dictionary<Object, (Vector3 position, float time)> lastMove = new();
        readonly Dictionary<Object, float> attackingAtHomeSince = new();
        readonly HashSet<Object> reportedStuck = new();
        readonly HashSet<Object> reportedHome = new();
        readonly HashSet<Factory> reportedBlocked = new();
        readonly List<string> problems = new();
        int errors, warnings;
        readonly List<string> firstErrors = new();

        MatchManager match;
        bool started;
        float startTime;
        float nextCheck;
        float nextShot;
        int shotIndex;
        int[] lastWave = new int[2];
        Camera shotCamera;
        RenderTexture shotTexture;

        void Awake()
        {
            Application.logMessageReceived += OnLog;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                errors++;
                if (firstErrors.Count < 15)
                    firstErrors.Add($"[{Elapsed:0}s] {type}: {message}\n{FirstLines(stackTrace, 4)}");
            }
            else if (type == LogType.Warning)
            {
                warnings++;
                if (firstErrors.Count < 15 && warnings <= 5)
                    firstErrors.Add($"[{Elapsed:0}s] Warning: {message}");
            }
            else if (message.StartsWith("[") && started)
            {
                Log(message);
            }
        }

        static string FirstLines(string text, int count)
        {
            var lines = text.Split('\n');
            return string.Join("\n", lines, 0, Mathf.Min(count, lines.Length));
        }

        float Elapsed => started ? Time.time - startTime : 0f;

        void Log(string line) => report.AppendLine($"[{Elapsed,6:0.0}s] {line}");

        void Problem(string line)
        {
            problems.Add($"[{Elapsed:0}s] {line}");
            Log("ПРОБЛЕМА: " + line);
        }

        void Update()
        {
            match = MatchManager.Instance;
            if (match == null || match.Player == null)
                return;

            if (!started)
            {
                // Строители появились (NavMesh готов) — за игрока тоже ИИ
                if (match.Player.builders.Count == 0)
                    return;
                var playerAi = match.gameObject.AddComponent<EnemyAI>();
                playerAi.Init(match, match.EnemyAiSettings);
                playerAi.ResetForMatch(match.Player, match.Enemy);
                playerAi.Begin();
                Time.timeScale = timeScale;
                started = true;
                startTime = Time.time;
                nextShot = Time.time + 5f;
                Directory.CreateDirectory(outputDir);
                Log($"Старт: сид {match.Arena.Seed}, ИИ за обе стороны, время ×{timeScale}, бонус {bonusIncome}/с");
                return;
            }

            if (bonusIncome > 0f)
            {
                match.Player.baseResource += bonusIncome * Time.deltaTime;
                match.Enemy.baseResource += bonusIncome * Time.deltaTime;
            }

            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 1f;
                CheckUnits();
                CheckWaves();
            }
            if (Time.time >= nextShot)
            {
                nextShot = Time.time + screenshotInterval;
                TakeShots();
            }
            if (match.IsOver || Elapsed >= durationSeconds)
                Finish();
        }

        readonly int[] lastVehiclesBuilt = new int[2];
        readonly int[] lastKnownVehicles = { 1, 1 };
        readonly bool[] hadFactory = new bool[2];

        void CheckWaves()
        {
            foreach (var f in new[] { match.Player, match.Enemy })
            {
                bool factory = false;
                foreach (var s in f.structures)
                    factory |= s != null && s.Factory != null && s.IsBuilt;
                if (factory && !hadFactory[f.team])
                    Log($"Сторона {f.team}: завод построен");
                hadFactory[f.team] = factory;
                if (f.knownVehicles.Count != lastKnownVehicles[f.team])
                {
                    lastKnownVehicles[f.team] = f.knownVehicles.Count;
                    Log($"Сторона {f.team}: открыта техника {string.Join(", ", f.knownVehicles)}");
                }
                if (f.vehiclesBuilt != lastVehiclesBuilt[f.team])
                {
                    lastVehiclesBuilt[f.team] = f.vehiclesBuilt;
                    var v = f.vehicles.Count > 0 ? f.vehicles[^1] : null;
                    string what = v != null ? $"{v.Def.name} в {Round(v.transform.position)}" : "";
                    Log($"Сторона {f.team}: выпущена машина ({f.vehiclesBuilt}) {what}");
                }
            }

            foreach (var ai in match.GetComponents<EnemyAI>())
            {
                // Сторона ИИ: первый — противник (team 1), добавленный — игрок (team 0)
                int team = ai == match.EnemyAI ? 1 : 0;
                if (ai.WaveNumber != lastWave[team])
                {
                    lastWave[team] = ai.WaveNumber;
                    Log($"Сторона {team}: волна {ai.WaveNumber}");
                }
            }
        }

        void CheckUnits()
        {
            foreach (var faction in new[] { match.Player, match.Enemy })
            {
                foreach (var u in faction.units)
                    if (u != null && u.IsAlive)
                        CheckAgent(u, u.GetComponent<NavMeshAgent>(), faction, u.Behavior,
                                   $"пехотинец {u.WeaponType}, отряд {u.Squad.Number} приказ {u.Squad.Order}, цель {Describe(u.Squad.AreaTarget)}, стреляет {u.Firing}");
                foreach (var v in faction.vehicles)
                    if (v != null && v.IsAlive)
                        CheckAgent(v, v.GetComponent<NavMeshAgent>(), faction, v.Behavior,
                                   $"{v.Def.name}, цель башни {Describe(v.Target)}");
                foreach (var b in faction.builders)
                    if (b != null)
                        CheckAgent(b, b.GetComponent<NavMeshAgent>(), faction, BarracksBehavior.Defend,
                                   $"строитель, стройка {(b.Target != null ? b.Target.name : "нет")}");
                foreach (var s in faction.structures)
                {
                    if (s == null || s.Factory == null)
                        continue;
                    if (s.Factory.ExitBlocked && reportedBlocked.Add(s.Factory))
                        Problem($"сторона {faction.team}: у завода в {Round(s.transform.position)} перекрыт выезд");
                    else if (!s.Factory.ExitBlocked)
                        reportedBlocked.Remove(s.Factory);
                }
            }
        }

        void CheckAgent(Component unit, NavMeshAgent agent, Faction faction, BarracksBehavior behavior, string details)
        {
            var position = unit.transform.position;
            var key = (Object)unit;

            // Застрял: должен ехать (путь дальше 1.5 м, не остановлен), но стоит
            bool shouldMove = agent.enabled && agent.isOnNavMesh && !agent.isStopped && !agent.pathPending &&
                              agent.hasPath && agent.remainingDistance > 1.5f;
            if (!lastMove.TryGetValue(key, out var last) || (position - last.position).magnitude > StuckMove || !shouldMove)
            {
                lastMove[key] = (position, Time.time);
                reportedStuck.Remove(key);
            }
            else if (Time.time - last.time > StuckWindow && reportedStuck.Add(key))
            {
                Problem($"сторона {faction.team}: застрял в {Round(position)} ({details}), до цели {agent.remainingDistance:0.0} м, " +
                        $"путь {agent.pathStatus}, назначение {Round(agent.destination)}");
            }

            // Атакующий долго сидит внутри своих стен
            if (behavior == BarracksBehavior.Attack && match.IsInsideWalls(position, faction))
            {
                if (!attackingAtHomeSince.TryGetValue(key, out float since))
                    attackingAtHomeSince[key] = Time.time;
                else if (Time.time - since > StayHomeLimit && reportedHome.Add(key))
                    Problem($"сторона {faction.team}: в атаке, но {StayHomeLimit:0} с не выходит с базы, в {Round(position)} ({details})");
            }
            else
            {
                attackingAtHomeSince.Remove(key);
            }
        }

        static string Describe(IDamageable target) =>
            Combat.IsAlive(target) ? $"{target.transform.name} в {Round(target.transform.position)}" : "нет";

        static string Round(Vector3 v) => $"({v.x:0},{v.y:0},{v.z:0})";

        // ---------- Кадры ----------

        void TakeShots()
        {
            var arena = match.Arena;
            Shot($"base0", arena.BaseOne, arena.GateOne);
            Shot($"base1", arena.BaseTwo, arena.GateTwo);

            // Где больше всего атакующих — туда третий кадр
            var sum = Vector3.zero;
            int count = 0;
            foreach (var faction in new[] { match.Player, match.Enemy })
            {
                foreach (var u in faction.units)
                    if (u != null && u.IsAlive && u.Behavior == BarracksBehavior.Attack)
                    {
                        sum += u.transform.position;
                        count++;
                    }
                foreach (var v in faction.vehicles)
                    if (v != null && v.IsAlive && v.Behavior == BarracksBehavior.Attack)
                    {
                        sum += v.transform.position;
                        count++;
                    }
            }
            if (count > 0)
                Shot("attack", sum / count, sum / count + Vector3.forward);
            // Техника крупным планом: первая живая машина
            foreach (var faction in new[] { match.Player, match.Enemy })
                foreach (var v in faction.vehicles)
                    if (v != null && v.IsAlive)
                    {
                        ShotClose($"vehicle{faction.team}", v.transform.position);
                        goto doneVehicles;
                    }
            doneVehicles:
            shotIndex++;
        }

        // Кадр сверху-сбоку: камера над точкой, смотрит на неё со стороны lookFrom
        void ShotClose(string label, Vector3 focus) => Shot(label, focus, focus + Vector3.forward, 7f, 6f);

        void Shot(string label, Vector3 focus, Vector3 lookFrom, float back = 18f, float up = 26f)
        {
            if (shotCamera == null)
            {
                var go = new GameObject("AutoTest Camera");
                shotCamera = go.AddComponent<Camera>();
                shotCamera.enabled = false;
                shotCamera.fieldOfView = 50f;
                shotCamera.farClipPlane = 400f;
                shotTexture = new RenderTexture(1280, 720, 24);
                shotCamera.targetTexture = shotTexture;
            }

            var away = focus - lookFrom;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
                away = Vector3.back;
            away.Normalize();
            shotCamera.transform.position = focus - away * back + Vector3.up * up;
            shotCamera.transform.LookAt(focus);
            shotCamera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = shotTexture;
            var image = new Texture2D(shotTexture.width, shotTexture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, shotTexture.width, shotTexture.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            string file = Path.Combine(outputDir, $"{shotIndex:00}_{Elapsed:000}s_{label}.png");
            File.WriteAllBytes(file, image.EncodeToJPG(80));
            Destroy(image);
        }

        // ---------- Итог ----------

        void Finish()
        {
            enabled = false;
            Time.timeScale = 1f;
            TakeShots();

            var summary = new StringBuilder();
            summary.AppendLine(match.IsOver
                ? $"Бой окончен: победила сторона {match.Winner.team} за {match.MatchTime:0} с"
                : $"Время теста вышло: {Elapsed:0} с игрового времени");
            foreach (var f in new[] { match.Player, match.Enemy })
                summary.AppendLine($"Сторона {f.team}: зданий {f.structures.Count}, построено {f.structuresBuilt}, потеряно {f.structuresLost}; " +
                                   $"бойцов {f.units.Count}, нанято {f.unitsHired}, потеряно {f.unitsLost}; " +
                                   $"техники {f.vehicles.Count}, выпущено {f.vehiclesBuilt}, потеряно {f.vehiclesLost}; " +
                                   $"открыто техники: {string.Join(", ", f.knownVehicles)}");
            summary.AppendLine($"Ошибок в консоли: {errors}, предупреждений: {warnings}");
            summary.AppendLine($"Проблем: {problems.Count}");
            foreach (var p in problems)
                summary.AppendLine("  " + p);
            if (firstErrors.Count > 0)
            {
                summary.AppendLine("Первые ошибки и предупреждения:");
                foreach (var e in firstErrors)
                    summary.AppendLine("  " + e);
            }
            summary.AppendLine();
            summary.AppendLine("Журнал:");
            summary.Append(report);

            File.WriteAllText(Path.Combine(outputDir, "report.txt"), summary.ToString());
            Application.Quit();
        }
    }
}
