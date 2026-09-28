using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameUtils : MonoBehaviour
{
    public static Unit GetNearestEnemyAnyUnit(Player player, Transform origin, float maxDist = 888)
    {
        var enemies = player.Enemy.allUnits;
        Unit nearest = null;
        var minDist = float.MaxValue;

        foreach (var unit in enemies)
        {
            var dist = Vector3.Distance(origin.position, unit.transform.position);
            if (maxDist < dist)
            {
                continue;
            }

            if (dist < minDist)
            {
                minDist = dist;
                nearest = unit;
            }
        }

        return nearest;
    }
}
