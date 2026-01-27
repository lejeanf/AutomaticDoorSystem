using Unity.Entities;
using UnityEngine;

namespace AutomaticDoorSystem
{
    public class DoorTriggerableAuthoring : MonoBehaviour
    {
        class Baker : Baker<DoorTriggerableAuthoring>
        {
            public override void Bake(DoorTriggerableAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<DoorTriggerableTag>(entity);

                AddComponent(entity, new EntityLayerComponent
                {
                    Layer = authoring.gameObject.layer
                });
            }
        }
    }
}
