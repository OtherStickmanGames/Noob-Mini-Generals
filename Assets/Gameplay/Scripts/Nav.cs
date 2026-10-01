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
    }
}
