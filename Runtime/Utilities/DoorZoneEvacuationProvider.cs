using System.Collections.Generic;
using jeanf.scenemanagement;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace AutomaticDoorSystem.Utilities
{
    /// <summary>
    /// Makes the doors answer SceneManagement's "how do I get the player out of this zone?".
    /// Drop it next to the other door bridges (the Door Management object) - one per scene is
    /// enough, it serves every door in the loaded SubScenes.
    /// <para>
    /// A door needs nothing but its exit anchor: its <see cref="DoorComponent.DoorId"/> is the
    /// number of the room it closes, so the room is resolved through
    /// <see cref="WorldManager.GetZoneByNumber"/>. Doors whose id matches no zone (corridor
    /// doors, pass-throughs) never answer.
    /// </para>
    /// <para>
    /// Doors are authored in SubScenes, so DoorAuthoring itself no longer exists at runtime: the
    /// anchor is baked into a <see cref="DoorZoneExit"/>, and this bridge reads it back. The
    /// dependency runs door -> SceneManagement (never the reverse), which is why the provider
    /// pushes itself into <see cref="ZoneEvacuationRegistry"/> instead of being looked up.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Automatic Door System/Door Zone Evacuation Provider")]
    public class DoorZoneEvacuationProvider : MonoBehaviour, IZoneEvacuationProvider
    {
        [Tooltip("Log every exit query and what it resolved to.")]
        [SerializeField] private bool isDebug = false;

        private EntityManager _entityManager;
        private EntityQuery _exitQuery;
        private bool _hasWorld;

        private void OnEnable()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogWarning("[DoorZoneEvacuationProvider] no ECS world yet - door exits will not be available " +
                                 "this session. Make sure this component lives in a scene loaded after the ECS world " +
                                 "is created.", this);
                return;
            }

            _entityManager = world.EntityManager;
            _exitQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<DoorZoneExit>(),
                ComponentType.ReadOnly<DoorComponent>(),
                ComponentType.ReadOnly<LocalToWorld>());
            _hasWorld = true;

            ZoneEvacuationRegistry.Register(this);
        }

        private void OnDisable()
        {
            ZoneEvacuationRegistry.Unregister(this);
            // The world can already be gone on quit; disposing a query of a dead world throws.
            if (_hasWorld && _entityManager.World is { IsCreated: true }) _exitQuery.Dispose();
            _hasWorld = false;
        }

        public bool TryGetExit(string zoneId, Vector3 from, IReadOnlyCollection<string> blockedZoneIds, out ZoneExit exit)
        {
            exit = default;
            if (!_hasWorld || string.IsNullOrEmpty(zoneId)) return false;
            if (_entityManager.World is not { IsCreated: true }) return false;

            var exits = _exitQuery.ToComponentDataArray<DoorZoneExit>(Allocator.Temp);
            var doors = _exitQuery.ToComponentDataArray<DoorComponent>(Allocator.Temp);
            var transforms = _exitQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

            var found = false;
            var bestDistanceSq = float.MaxValue;
            var bestDoorId = 0;

            try
            {
                for (var i = 0; i < exits.Length; i++)
                {
                    // The door id IS the room number: door 2005 closes the zone numbered 2005.
                    var room = WorldManager.GetZoneByNumber(doors[i].DoorId);
                    if (room == null || room.id != zoneId) continue;

                    var worldPosition = math.transform(transforms[i].Value, exits[i].ExitLocalPosition);
                    var distanceSq = math.distancesq(worldPosition, (float3)from);
                    if (found && distanceSq >= bestDistanceSq) continue;

                    var worldForward = math.rotate(transforms[i].Value, exits[i].ExitLocalForward);
                    worldForward.y = 0f;
                    var rotation = math.lengthsq(worldForward) > 1e-6f
                        ? Quaternion.LookRotation(math.normalize(worldForward), Vector3.up)
                        : Quaternion.identity;

                    bestDistanceSq = distanceSq;
                    bestDoorId = doors[i].DoorId;
                    // The anchor's own zone is not authored, so nothing is claimed about where it
                    // lands: the blocked-zone filter only applies to providers that do know.
                    exit = new ZoneExit(worldPosition, rotation, string.Empty, $"door {bestDoorId}");
                    found = true;
                }
            }
            finally
            {
                exits.Dispose();
                doors.Dispose();
                transforms.Dispose();
            }

            if (isDebug)
                Debug.Log(found
                    ? $"[DoorZoneEvacuationProvider] exit out of '{zoneId}' at {exit.Position}, through door {bestDoorId}."
                    : $"[DoorZoneEvacuationProvider] no door with an exit anchor carries the number of zone '{zoneId}'.",
                    this);

            return found;
        }
    }
}
