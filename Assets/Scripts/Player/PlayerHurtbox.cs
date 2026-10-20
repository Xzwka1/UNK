using UnityEngine;

/// <summary>
/// Lives on the child "Hurtbox" trigger collider. Forwards contact with any Hazard2D to PlayerRespawn.
/// Only this small trigger (not the solid hitbox) is used for hazard detection.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class PlayerHurtbox : MonoBehaviour
{
    private PlayerRespawn respawn;

    private void Awake()
    {
        respawn = GetComponentInParent<PlayerRespawn>();
        GetComponent<BoxCollider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other) => CheckHazard(other);
    private void OnTriggerStay2D(Collider2D other) => CheckHazard(other);

    private void CheckHazard(Collider2D other)
    {
        if (respawn == null || respawn.IsDead) return;
        if (other.GetComponentInParent<Hazard2D>() != null) respawn.Kill();
    }
}
