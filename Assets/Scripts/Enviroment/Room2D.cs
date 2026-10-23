using UnityEngine;

/// <summary>
/// Defines a 2D room zone.
/// When the player enters this zone:
/// 1. Updates CameraController2D bounds so the camera stays confined inside this room.
/// 2. Updates PlayerRespawn's active spawnPoint (checkpoint) to this room's spawn point.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Room2D : MonoBehaviour
{
    [Header("Checkpoint")]
    [Tooltip("Point where the player respawns if they die in this room. If empty, uses this room's transform.")]
    [SerializeField] private Transform spawnPoint;

    [Header("Camera")]
    [Tooltip("Optional. Auto-found if empty.")]
    [SerializeField] private CameraController2D camController;

    private Collider2D roomCollider;

    private void Awake()
    {
        roomCollider = GetComponent<Collider2D>();
        roomCollider.isTrigger = true;

        if (camController == null)
        {
            camController = FindAnyObjectByType<CameraController2D>();
        }
    }

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Check if the entering object is the player
        var player = other.GetComponentInParent<PlayerController2D>();
        if (player == null) return;

        // 1. Confine camera to this room's world bounds
        if (camController == null)
        {
            camController = FindAnyObjectByType<CameraController2D>();
        }

        if (camController != null && roomCollider != null)
        {
            Bounds b = roomCollider.bounds;
            camController.SetRoomBounds(b.min, b.max);
        }

        // 2. Update player's respawn checkpoint
        var respawn = other.GetComponentInParent<PlayerRespawn>();
        if (respawn != null)
        {
            respawn.spawnPoint = spawnPoint != null ? spawnPoint : transform;
        }
    }

    // Visual helpers in the Scene view for level design
    private void OnDrawGizmos()
    {
        var col = GetComponent<Collider2D>();
        if (col != null)
        {
            // Room bounds wireframe (Cyan)
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.4f);
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }

        // Spawn point marker (Yellow)
        if (spawnPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(spawnPoint.position, 0.25f);
            Gizmos.DrawLine(spawnPoint.position, spawnPoint.position + Vector3.up * 0.5f);
        }
    }
}
