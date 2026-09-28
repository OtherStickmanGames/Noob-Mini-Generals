using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine;
using System.Linq;

public class Building : MonoBehaviour
{
    [SerializeField] BuildingType buildingType;
    [SerializeField] Transform unitSpawnPoint;
    [SerializeField] List<ResearchData> researchesData;
    
    [field:SerializeField]
    public Team Team { get; set; }
    [field:SerializeField]
    public int Workers { get; set; }
    public BuildingType BuildingType => buildingType;
    public Transform UnitSpawnPoint => unitSpawnPoint;
    public HealthComponent Health { get; private set; }
    public Player General { get; private set; }
    public List<ResearchData> ResearchesData => researchesData;


    [Space(18)]

    public UnityEvent<Building> onAnniged;


    float goldExtractionRate;

    public void Init(Player player)
    {
        General = player;
        Team = player.Team;
        gameObject.name += $" {Team}";

        Health = GetComponent<HealthComponent>();

        if (Health)
        {
            Health.valueChanged += Health_Changed;
        }

        CheckAvailableCards();
    }


    private void Health_Changed(HealthComponent health)
    {
        if(health.Value <= 0)
        {
            onAnniged?.Invoke(this);
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (General.WaitStartGame)
            return;

        if (buildingType == BuildingType.Main)
        {
            goldExtractionRate += Time.deltaTime;

            if (goldExtractionRate > 1f)
            {
                General.CountGold++;
                goldExtractionRate = 0;
            }
        }
    }

    void CheckAvailableCards()
    {
        var mainBuildHealth = General.usedCards.Find(c => c.cardType == CardType.MainBuildIncreaseHealth);

        if (buildingType == BuildingType.Main && mainBuildHealth != null)
        {
            Health.Init(Health.MaxValue + mainBuildHealth.values[0]);
        }
    }
}

public enum BuildingType
{
    Main,
    Pit,
    Turrel,
    Barracks,
    MachineFactory,
}

public enum Research
{
    MiddleTank,
}

[System.Serializable]
public class ResearchData
{
    public Research research;
    public int needGold;
    public int needIron;
}
