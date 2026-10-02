using UnityEngine;
using UnityEngine.AI;

namespace Generals
{
    /// <summary>
    /// Фильтры запросов к NavMesh. У пехоты (и строителей) и у техники свои NavMesh (у техники проходы
    /// уже на её радиус), поэтому каждый запрос — с типом агента: запрос только с маской областей мог
    /// бы взять NavMesh не того агента.
    /// </summary>
    public static class Nav
    {
        public static NavMeshQueryFilter Infantry => new()
        {
            agentTypeID = MatchManager.Instance.ArenaNav.AgentTypeID,
            areaMask = NavMesh.AllAreas,
        };

        public static NavMeshQueryFilter Vehicles => new()
        {
            agentTypeID = MatchManager.Instance.ArenaNav.VehicleAgentTypeID,
            areaMask = NavMesh.AllAreas,
        };

        static NavMeshPath unstickPath;

        /// <summary>
        /// Агент застрял на отрезанном куске NavMesh (рядом вырезала себя стройка, и щель между зданием
        /// и стеной перестала быть проходимой): найти рядом, до 2.5 м, место на сетке, от которого есть
        /// полный путь до ворот своей базы, и перенести агента туда. true — перенесён
        /// </summary>
        public static bool Unstick(NavMeshAgent agent, NavMeshQueryFilter filter, Vector3 gate)
        {
            if (!NavMesh.SamplePosition(gate, out var gateHit, 4f, filter))
                return false;
            unstickPath ??= new NavMeshPath();
            var from = agent.transform.position;
            // От текущего места путь до ворот есть — не застрял (дело в цели, а не в месте)
            if (NavMesh.CalculatePath(from, gateHit.position, filter, unstickPath) &&
                unstickPath.status == NavMeshPathStatus.PathComplete)
                return false;
            foreach (float radius in new[] { 0.75f, 1.5f, 2.5f })
            {
                for (int k = 0; k < 8; k++)
                {
                    var dir = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                    if (!NavMesh.SamplePosition(from + dir * radius, out var hit, 0.6f, filter))
                        continue;
                    if (NavMesh.CalculatePath(hit.position, gateHit.position, filter, unstickPath) &&
                        unstickPath.status == NavMeshPathStatus.PathComplete)
                    {
                        agent.Warp(hit.position);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
