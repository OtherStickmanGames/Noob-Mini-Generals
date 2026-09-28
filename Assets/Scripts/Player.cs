using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

public class Player : MonoBehaviour
{
    [SerializeField] Team team;
    [SerializeField] List<Building> buildings;
    [SerializeField] List<UnitSpawnData> unitsData;
    [SerializeField] List<BuildingSpawnData> buildingsData;

    [Space]

    [SerializeField] Transform startPointGenerateBuildPoints;
    [SerializeField] Transform pointPrefab;
    [SerializeField] int placeWidthRange = 10;
    [SerializeField] int placeLengthRange = 10;

    public List<Unit> allUnits;
    public List<Vector3> turrelBuildPoses;
    public List<Research> researched;
    public List<Card> usedCards;

    public Team Team => team;
    public List<Building> Buildings => buildings;
    public List<UnitSpawnData> UnitsData => unitsData;
    public List<Building> AllBarracks => buildings.FindAll(b => b.BuildingType == BuildingType.Barracks);
    public List<Building> AllMachineFactoryes => buildings.FindAll(b => b.BuildingType == BuildingType.MachineFactory);


    public Player Enemy { get; private set; }
    public int CountIron { get; set; } = 10;
    public int CountGold { get; set; } = 130;
    public bool WaitStartGame { get; set; } = true;


    List<GameObject> tempos = new();

    private void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        var players = FindObjectsOfType<Player>().ToList();
        Enemy = players.Find(p => p.Team != team);

        foreach (var building in buildings)
        {
            building.Init(this);
            building.onAnniged.AddListener(Building_Anniged);
        }

        GenerateBuildPoints();
    }

    private void Building_Anniged(Building building)
    {
        buildings.Remove(building);
        LeanTween.delayedCall(1f, GenerateBuildPoints);

        if (building.BuildingType == BuildingType.Main)
        {
            var vinTeam = team == Team.One ? Team.Two : Team.One;
            EventsHolder.onVictory?.Invoke(vinTeam);
        }
    }

    public void SpawnUnit(UnitType unitType, TrooperType trooperType)
    {
        var data = unitsData.Find(u => u.prefab.UnitType == unitType && u.prefab.TrooperType == trooperType);

        var prefab = data.prefab;

        var pos = buildings[0].UnitSpawnPoint.position;
        if (trooperType == TrooperType.Middle)
        {
            pos = buildings.Find(b => b.BuildingType == BuildingType.Barracks)?.UnitSpawnPoint.position ?? pos;
        }
        if (trooperType == TrooperType.HeavyTrooper)
        {
            pos = buildings.Find(b => b.BuildingType == BuildingType.Barracks)?.UnitSpawnPoint.position ?? pos;
        }
        if (trooperType == TrooperType.LightTank)
        {
            pos = buildings.Find(b => b.BuildingType == BuildingType.MachineFactory)?.UnitSpawnPoint.position ?? pos;
        }

        var unit = Instantiate(prefab, pos, Quaternion.identity);
        unit.Init(team, this);

        allUnits.Add(unit);
        CountGold -= data.needGold;
        CountIron -= data.needIron;
    }

    public void Build(BuildingType buildingType, Vector3 pos)
    {
        var data = GetBuildingData(buildingType);

        var prefab = data.prefab;
        var building = Instantiate(prefab, pos, Quaternion.LookRotation(transform.forward));
        building.Init(this);
        building.onAnniged.AddListener(Building_Anniged);

        if (buildingType == BuildingType.Turrel)
        {

        }

        CountGold -= data.needGold;
        CountIron -= data.needIron;

        
        buildings.Add(building);

        LeanTween.delayedCall(0.1f, GenerateBuildPoints);
    }

    public void SpendResearch(Research research)
    {
        var data = buildings.Find(b => b.ResearchesData.Any(r => r.research == research)).ResearchesData.Find(d => d.research == research);

        CountGold -= data.needGold;
        CountIron -= data.needIron;

        researched.Add(research);
    }

    public BuildingSpawnData GetBuildingData(BuildingType buildingType)
    {
        return buildingsData.Find(d => d.prefab.BuildingType == buildingType);
    }

    public Building GetPit()
    {
        return buildings.Find(b => b.BuildingType == BuildingType.Pit);
    }

    public Building GetMainBuilding()
    {
        return buildings.Find(b => b.BuildingType == BuildingType.Main);
    }

    void GenerateBuildPoints()
    {
        tempos.ForEach(go => Destroy(go));
        tempos = new();

        turrelBuildPoses = new();
        int step = 2;
        for (int x = -placeWidthRange; x <= placeWidthRange; x += step)
        {
            for (int z = 0; z < placeLengthRange; z += step)
            {
                var zOffset = transform.forward;
                var pos = new Vector3(x, 0.5f, startPointGenerateBuildPoints.position.z) + zOffset * z;

                var hits = Physics.BoxCastAll(pos, Vector3.one * 0.8f, transform.up * 0.5f);
                var hit = hits.ToList().Find(h => !h.collider.CompareTag("Ground") && !h.collider.CompareTag("Unit"));
                if (hit.collider)
                {
                    //print(hit.collider.name);
                    continue;
                }

                var point = Instantiate(pointPrefab, pos, Quaternion.identity);
                point.name += $" {team}";
                turrelBuildPoses.Add(pos);
                tempos.Add(point.gameObject);
            }
        }
    }
}

[System.Serializable]
public class UnitSpawnData
{
    public string name;
    public Unit prefab;
    public int needGold;
    public int needIron;
}

[System.Serializable]
public class BuildingSpawnData
{
    public string name;
    public Building prefab;
    public int needGold;
    public int needIron;
}

