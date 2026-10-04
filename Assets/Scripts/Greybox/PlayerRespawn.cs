using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Death loop: when stress overloads or the Hurtbox touches a Hazard2D, disable movement, wait a short delay,
/// move the player back to the start point, reset stress and re-enable movement.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(PlayerController2D), typeof(PlayerStress))]
public class PlayerRespawn : MonoBehaviour
{
    [Tooltip("Seconds between death and respawn (fast respawn).")]
    [SerializeField] private float respawnDelay = 0.2f;

    [Tooltip("Optional. If empty, the position the player has at Start() is used. GreyboxLevelGenerator assigns this automatically.")]
    public Transform spawnPoint;

    public bool IsDead { get; private set; }
    public event Action Died;
    public event Action Respawned;

    private Rigidbody2D rb;
    private PlayerController2D controller;
    private PlayerStress stress;
    private Vector2 startPosition;

    private Vector2 SpawnPosition => spawnPoint != null ? (Vector2)spawnPoint.position : startPosition;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        controller = GetComponent<PlayerController2D>();
        stress = GetComponent<PlayerStress>();
    }

    private void Start() => startPosition = transform.position;

    private void OnEnable() => GetComponent<PlayerStress>().Overloaded += OnOverloaded;
    private void OnDisable() => GetComponent<PlayerStress>().Overloaded -= OnOverloaded;

    private void OnOverloaded() => Kill();

    /// <summary>Kills the player and starts the respawn sequence. Safe to call repeatedly.</summary>
    public void Kill()
    {
        if (IsDead) return;
        IsDead = true;

        controller.SetControlEnabled(false); // movement off immediately
        Died?.Invoke();
        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        // Kill() is usually called from a physics callback; wait a frame before touching the body.
        yield return null;
        rb.linearVelocity = Vector2.zero;
        rb.simulated = false;

        yield return new WaitForSeconds(respawnDelay);

        Vector2 p = SpawnPosition;
        transform.position = new Vector3(p.x, p.y, transform.position.z);
        rb.position = p;

        stress.ResetStress();
        controller.ResetState();
        rb.simulated = true;

        IsDead = false;
        controller.SetControlEnabled(true);
        Respawned?.Invoke();
    }
}
