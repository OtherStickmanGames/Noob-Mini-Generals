using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine;
using System.Linq;
using System;
using Random = UnityEngine.Random;

public class AIBehaviour : MonoBehaviour
{
    public Dictionary<TaskType, Action> actionsSet;

    /// <summary>
    /// Для сохранения входных данных для нейронки
    /// </summary>
    Dictionary<InputKey, int> inputActionSet;
    List<InputData> savedInputData;
    List<InputKey> currentInputKey;
    List<Action> tasks;

    Action retried = null;
    Player player;
    NeuronNetwork neuronNetwork;

    TaskType prevAction;

    float playtime;
    int countSeconds;
    float retriedTimer;
    float delayBetweenTasks;// Задержка между принятием решений
    bool taskCompleted = true;
    bool inputSetWrited;
    int idxTask = 0;
    int countTeachedNeuron;
    /// <summary>
    /// Количество повторов одно и того же действия
    /// </summary>
    int countRetriedAction;
    bool needReteaching;
    bool neuronReady;
    bool gameover;

    public int CountTryedOnSet
    {
        get
        {
            var key = $"CountTryed{player.Team}";
            if (PlayerPrefs.HasKey(key))
            {
                return PlayerPrefs.GetInt(key);
            }
            else
            {
                return 11;
            }
        }

        set
        {
            var key = $"CountTryed{player.Team}";
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
        }
    }

    private void Start()
    {
        player = GetComponent<Player>();
        neuronNetwork = GetComponent<NeuronNetwork>();

        savedInputData = JsonXyeson.LoadData(player.Team);

        InitActionsSet();
        ChooseCardSet();

        inputActionSet = new();
        currentInputKey = new();
        tasks = new();
        tasks.Add(actionsSet[TaskType.SpawnWorker]);
        //tasks.Add(actionsSet[TaskType.SpawnWorker]);
        //tasks.Add(actionsSet[TaskType.SpawnSimple]);
        EventsHolder.onVictory.AddListener(Victory);
        EventsHolder.neuronTeached.AddListener(Neuron_Teached);
    }

    private void Neuron_Teached()
    {
        countTeachedNeuron++;
        if (countTeachedNeuron == 2)
        {
            neuronReady = true;
            player.WaitStartGame = false;
        }
    }

    private void Victory(Team team)
    {
        gameover = true;

        if (team != player.Team)
        {
            int countChangedActions = 0;
            foreach (var data in savedInputData)
            {
                if(data.countWin == 0)
                {
                    data.actionIndex = GetRandomActionIdx(data);
                    countChangedActions++;
                }

                if (data.countWin != 0)
                {
                    data.countWin--;
                }
                //if (data.winable)
                //{
                //    var randomIdxSet = Random.Range(0, savedInputData.Count);
                //    savedInputData[randomIdxSet].actionIndex = GetRandomActionIdx(data);
                //    print($"Был заменен {randomIdxSet} набор");
                //    break;
                //}
                //else
                //{
                //    data.actionIndex = GetRandomActionIdx(data);
                //}
            }
            CountTryedOnSet++;
            if (CountTryedOnSet > 8)
            {
                CheckNewInputKeys();
                CountTryedOnSet = 0;
            }

            if(countChangedActions == 0 && CountTryedOnSet >= 3)
            {
                CheckNewInputKeys();
                CountTryedOnSet = 0;
            }
            //print($"Записан новый набор данных для команды {player.Team}");
        }
        else
        {
            foreach (var data in savedInputData)
            {
                data.winable = true;
                data.countWin++;
            }
        }

        JsonXyeson.SaveFile(player.Team, savedInputData);

        print($"Победила команда {team} # Добавлено новых наборов {inputActionSet.Count} # необходимость переобычения {needReteaching}");

        if (player.Team == Team.One)
        {
            LeanTween.delayedCall(8f, () => SceneManager.LoadScene(0));
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.I))
        {
            //BuildTurrelNearPit();
            //BuildBarracks();
            //BuildMachineFactory();
            SpendResearchMiddleTank();
        }

        if (gameover)
            return;

        if (!neuronReady)
            return;

        delayBetweenTasks += Time.deltaTime;


        if (taskCompleted && tasks.Count > idxTask)
        {
            taskCompleted = false;

            tasks[idxTask]();
            idxTask++;
        }

        if (retried != null)
        {
            retriedTimer += Time.deltaTime;

            if (retriedTimer > 1.3f)
            {
                // Если пытается повторить много раз невыполнимое действие
                countRetriedAction++;
                if (countRetriedAction > 8)
                {
                    var key = GetInputKey();
                    retried = actionsSet[(TaskType)GetRandomActionIdx(ToInputData(key))];
                    countRetriedAction = 0;
                }

                retried();
                retriedTimer = 0;
            }
        }

        if (delayBetweenTasks > 1.8f)
        {
            if (!(tasks.Count > idxTask))
            {
                var idx = neuronNetwork.GetActionIdx(GetInputKey());
                
                // Проверка на повторяемое действие
                if (prevAction == (TaskType)idx)
                {
                    countRetriedAction++;
                    print($"{player.Team} !!! Действие повторилось {countRetriedAction} раз !!!");
                    if(countRetriedAction > 10)
                    {
                        needReteaching = true;
                        var enemyAI = player.Enemy.GetComponent<AIBehaviour>();
                        if (enemyAI)
                        {
                            enemyAI.needReteaching = true;
                        }
                    }
                }
                else
                {
                    countRetriedAction = 0;
                }

                if (needReteaching)
                {
                    idx = Random.Range(0, actionsSet.Count);
                    needReteaching = false;
                }

                prevAction = (TaskType)idx;
                tasks.Add(actionsSet[(TaskType)idx]);
                delayBetweenTasks = 0;
            }
        }

        playtime += Time.deltaTime;
        GenerateInputSet();
    }

    InputKey GetInputKey()
    {
        InputKey key = new()
        {
            playtime = countSeconds,
            gold = player.CountGold,
            countWorkers = player.allUnits.FindAll(u => u.UnitType == UnitType.Worker).Count,
            countSimple = player.allUnits.FindAll(u => u.UnitType == UnitType.Trooper).Count,
            iron = player.CountIron,
            countTurrels = player.Buildings.FindAll(b => b.BuildingType == BuildingType.Turrel).Count,
            countBarracks = player.Buildings.FindAll(b => b.BuildingType == BuildingType.Barracks).Count,
            countMachineFactoryes = player.Buildings.FindAll(b => b.BuildingType == BuildingType.MachineFactory).Count
        };

        return key;
    }

    void GenerateInputSet()
    {
        if (playtime > countSeconds)
        {
            countSeconds++;

            if (countSeconds % 3 == 0)
            {
                var inputKey = GetInputKey();
                var maxPlaytime = savedInputData.Max(d => d.inputs[(int)InputType.Playtime]);
                if (maxPlaytime < countSeconds)
                {
                    var idx = Random.Range(0, actionsSet.Count);

                    inputActionSet.Add(inputKey, idx);
                }

                currentInputKey.Add(inputKey);
            }
        }

    }

    void SpawnUnit(UnitType unitType, TrooperType trooperType)
    {
        var data = player.UnitsData.Find(u => u.prefab.UnitType == unitType && u.prefab.TrooperType == trooperType);

        if (CheckHaveRes(data))
        {
            player.SpawnUnit(unitType, trooperType);
            taskCompleted = true;
            retried = null;
        }
        else
        {
            retried = tasks[idxTask - 1];
        }
    }

    void SpawnWorker()
    {
        SpawnUnit(UnitType.Worker, TrooperType.None);
    }

    void SpawnSimple()
    {
        SpawnUnit(UnitType.Trooper, TrooperType.Simple);
    }

    private void SpawnMiddleTrooper()
    {
        SpawnUnit(UnitType.Trooper, TrooperType.Middle);
    }

    private void SpawnHeavyTrooper()
    {
        SpawnUnit(UnitType.Trooper, TrooperType.HeavyTrooper);
    }

    private void SpawnLightTank()
    {
        SpawnUnit(UnitType.Trooper, TrooperType.LightTank);
    }

    private void SpawnMiddleTank()
    {
        SpawnUnit(UnitType.Trooper, TrooperType.MiddleTank);
    }

    bool CheckHaveRes(UnitSpawnData data)
    {
        var haveGold = data.needGold <= player.CountGold;
        var haveIron = data.needIron <= player.CountIron;

        var available = true;
        if (data.prefab.TrooperType == TrooperType.Middle)
        {
            available = player.AllBarracks.Count > 0;
        }

        if (data.prefab.TrooperType == TrooperType.HeavyTrooper)
        {
            available = player.AllBarracks.Count > 0;
        }

        if (data.prefab.TrooperType == TrooperType.LightTank)
        {
            available = player.AllMachineFactoryes.Count > 0;
        }

        if (data.prefab.TrooperType == TrooperType.MiddleTank)
        {
            var existResearch = player.researched.Any(r => r == Research.MiddleTank);
            available = player.AllMachineFactoryes.Count > 0 && existResearch;
        }

        return haveGold && haveIron && available;
    }

    private void SpendResearchMiddleTank()
    {
        SpendResearch(Research.MiddleTank);
    }

    private void SpendResearch(Research research)
    {
        if (AvailableResearch(research))
        {
            player.SpendResearch(research);
            taskCompleted = true;
            retried = null;
        }
        else
        {
            retried = tasks[idxTask - 1];
        }
    }

    private bool AvailableResearch(Research research)
    {
        if (player.researched.Any(r => r == research))
        {
            return false;
        }

        var needBuilding = player.Buildings.Find(b => b.ResearchesData.Any(d => d.research == research));
        if (needBuilding)
        {
            var data = needBuilding.ResearchesData.Find(d => d.research == research);
            if (data.needGold <= player.CountGold && data.needIron <= player.CountIron)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return false;
        }
    }

    private void SetDefMode()
    {
        player.allUnits.ForEach(u => u.UnitMode = UnitMode.Def);
        taskCompleted = true;
        retried = null;
    }

    private void SetAttackUnitMode()
    {
        player.allUnits.ForEach(u => u.UnitMode = UnitMode.AttackAnyUnits);
        taskCompleted = true;
        retried = null;
    }

    private void SetAttackBuildingMode()
    {
        player.allUnits.ForEach(u => u.UnitMode = UnitMode.AttackAnyBuilds);
        taskCompleted = true;
        retried = null;
    }

    private void SetAttackAnyTargetMode()
    {
        player.allUnits.ForEach(u => u.UnitMode = UnitMode.AttackAnyTarget);
        taskCompleted = true;
        retried = null;
    }

    private void SetDefPitModeForSingle()
    {
        var allUnits = player.allUnits.FindAll(u => u.UnitType != UnitType.Worker);
        if (allUnits.Count > 0)
        {
            var randomUnit = allUnits[Random.Range(0, allUnits.Count)];
            randomUnit.UnitMode = UnitMode.DefPit;
        }
        taskCompleted = true;
        retried = null;
    }

    private void BuildTurrelNearPit()
    {
        if (!CheckAvailableBuild(BuildingType.Turrel))
        {
            if (tasks.Count > 0 && idxTask > 1)// Для тестов пришлось добавить
            {
                retried = tasks[idxTask - 1];
            }
            return;
        }

        var pit = player.GetPit();
        var posNearPit = pit.transform.forward + pit.transform.position;

        float minDist = float.MaxValue;
        Vector3 targetPos = default;
        foreach (var item in player.turrelBuildPoses)
        {
            var dist = Vector3.Distance(item, posNearPit);
            if(minDist > dist)
            {
                targetPos = item;
                minDist = dist;
            }
        }

        player.Build(BuildingType.Turrel, targetPos);

        taskCompleted = true;
        retried = null;
    }


    public void BuildBarracks()
    {
        if (!CheckAvailableBuild(BuildingType.Barracks))
        {
            if (tasks.Count > 0 && idxTask > 1)// Для тестов пришлось добавить
            {
                retried = tasks[idxTask - 1];
            }
            return;
        }

        // Пока добавим ограничение на одну еденицу
        if (player.AllBarracks.Count >= 1)
        {
            taskCompleted = true;
            retried = null;
            return;
        }

        var main = player.GetMainBuilding();
        var posNearMain = (main.transform.right * -1.8f) + main.transform.position;

        float minDist = float.MaxValue;
        Vector3 targetPos = default;
        foreach (var item in player.turrelBuildPoses)
        {
            var dist = Vector3.Distance(item, posNearMain);
            if (minDist > dist)
            {
                targetPos = item;
                minDist = dist;
            }
        }

        player.Build(BuildingType.Barracks, targetPos);

        taskCompleted = true;
        retried = null;
    }

    private void BuildMachineFactory()
    {
        if (!CheckAvailableBuild(BuildingType.MachineFactory))
        {
            if (tasks.Count > 0 && idxTask > 1)// Для тестов пришлось добавить
            {
                retried = tasks[idxTask - 1];
            }
            return;
        }

        // Пока добавим ограничение на одну еденицу
        if (player.AllMachineFactoryes.Count >= 1)
        {
            taskCompleted = true;
            retried = null;
            return;
        }

        var main = player.GetMainBuilding();
        var posNearMain = (main.transform.right * 1.8f) + main.transform.position;

        float minDist = float.MaxValue;
        Vector3 targetPos = default;
        foreach (var item in player.turrelBuildPoses)
        {
            var dist = Vector3.Distance(item, posNearMain);
            if (minDist > dist)
            {
                targetPos = item;
                minDist = dist;
            }
        }

        player.Build(BuildingType.MachineFactory, targetPos);

        taskCompleted = true;
        retried = null;
    }

    bool CheckAvailableBuild(BuildingType buildingType)
    {
        var availablePoints = player.turrelBuildPoses.Count > 0;
        var data = player.GetBuildingData(buildingType);
        return player.CountGold >= data.needGold && player.CountIron >= data.needIron && availablePoints;
    }

    int GetRandomActionIdx(InputData inputData)
    {
        var idx = Random.Range(0, actionsSet.Count);

        var countSimple = inputData.inputs[(int)InputType.CountSimple];

        var actionType = (TaskType)idx;
        while 
        (
            actionType == TaskType.SetDefMode
         || actionType == TaskType.SetAttackUnitMode
         || actionType == TaskType.SetAttackBuildingMode
         || actionType == TaskType.SetDefPitModeForSingle
         //|| actionType == TaskType.SetAttackAnyTargetMode
        )
        {
            if (countSimple == 0)
            {
                idx = Random.Range(0, actionsSet.Count);
                actionType = (TaskType)idx;
            }
            else
            {
                break;
            }
        }

        print($"{actionType}");

        return idx;
    }

    void CheckNewInputKeys()
    {
        List<InputData> newInputs = new();

        foreach (var key in currentInputKey)
        {
            //print($"{key.playtime}");
            var data = savedInputData.Find
            (
                d => 
                Mathf.Abs(d.inputs[(int)InputType.Playtime] - key.playtime) < 0.1f
             && Mathf.Abs(d.inputs[(int)InputType.CountSimple] - key.countSimple) < 0.1f
             && Mathf.Abs(d.inputs[(int)InputType.CountWorkers] - key.countWorkers) < 0.1f
             && Mathf.Abs(d.inputs[(int)InputType.Gold] - key.gold) < 10
             && Mathf.Abs(d.inputs[(int)InputType.Iron] - key.iron) < 30
             && Mathf.Abs(d.inputs[(int)InputType.CountTurrels] - key.countTurrels) < 0.1f
             && Mathf.Abs(d.inputs[(int)InputType.CountBarracks] - key.countBarracks) < 0.1f
             && Mathf.Abs(d.inputs[(int)InputType.CountMachineFactoryes] - key.countMachineFactoryes) < 0.1f
            );
            
            if (data == null)
            {
                //print($"нашел такой же плейтам {key.playtime} ### {data.inputs[(int)InputType.Playtime]}");
                newInputs.Add(ToInputData(key));
                continue;
            }
        }

        // Добавляем один случайный новый набор входных данных
        if (newInputs.Count > 0)
        {
            savedInputData.Add(newInputs[Random.Range(0, newInputs.Count)]);
        }
    }

    InputData ToInputData(InputKey key)
    {
        float[] inputs = key.ToArra();

        InputData data = new() { inputs = inputs.ToList() };
        data.actionIndex = GetRandomActionIdx(data);

        return data;
    }

    void ChooseCardSet()
    {
        List<Card> cards = new();
        var cardsHolder = Resources.Load<CardsHolder>("Cards Holder");
        //print(cardsHolder);
        cards.Add(cardsHolder.GetCardData(CardType.TroopersIncreaseHealth).ToCardForAI());
        cards.Add(cardsHolder.GetCardData(CardType.TurrelsAttackDistance).ToCardForAI());
        cards.Add(cardsHolder.GetCardData(CardType.MainBuildIncreaseHealth).ToCardForAI());

        player.usedCards = cards;
    }

    void InitActionsSet()
    {
        actionsSet = new()
        {
            { TaskType.SpawnWorker, () => SpawnWorker() },
            { TaskType.SpawnSimple, () => SpawnSimple() },
            { TaskType.SetDefMode, () => SetDefMode() },
            { TaskType.SetAttackUnitMode, () => SetAttackUnitMode() },
            { TaskType.SetAttackBuildingMode, () => SetAttackBuildingMode() },
            { TaskType.SetAttackAnyTargetMode, () => SetAttackAnyTargetMode() },
            { TaskType.SetDefPitModeForSingle, () => SetDefPitModeForSingle() },
            { TaskType.BuildTurrelNearPit, () => BuildTurrelNearPit() },
            { TaskType.BuildBarracks, () => BuildBarracks() },
            { TaskType.BuildMachineFactory, () => BuildMachineFactory() },
            { TaskType.SpawnMiddleTrooper, () => SpawnMiddleTrooper() },
            { TaskType.SpawnLightTank, () => SpawnLightTank() },
            { TaskType.SpendResearchMiddleTank, () => SpendResearchMiddleTank() },
            { TaskType.SpawnMiddleTank, () => SpawnMiddleTank() },
            { TaskType.SpawnHeavyTrooper, () => SpawnHeavyTrooper() },
        };
    }

    // Правило добавления нового действия
    // 1) Добавить новый энам в TaskType
    // 2) Добаить новый делегат в список actionSet
    // 3) Добавить при необходимости новый UnitMode
    // 4) Добавить при необходимости исключение в GetRandomActionIdx()
    // 5) Добавить при необходимости новые входные данные, в том числе и 
    // в место где создаются дефолтные ИнпутСеты -> JsonXyeson и Нейронке внизу
    // и чекни метод GetInputKey
    // и еще метод CheckNewInputKeys 
}

public enum TaskType : int
{
    SpawnWorker             = 0,
    SpawnSimple             = 1,
    SetDefMode              = 2,
    SetAttackUnitMode       = 3,
    SetAttackBuildingMode   = 4,
    SetAttackAnyTargetMode  = 5,
    SetDefPitModeForSingle  = 6,
    BuildTurrelNearPit      = 7,
    BuildBarracks           = 8,
    BuildMachineFactory     = 9,
    SpawnMiddleTrooper      = 10,
    SpawnLightTank          = 11,
    SpendResearchMiddleTank = 12,
    SpawnMiddleTank         = 13,
    SpawnHeavyTrooper       = 14,
}
