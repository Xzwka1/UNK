using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Death loop: when stress overloads or the Hurtbox touches a Hazard2D, disable movement, wait a short delay
/// so the death animation (Dead or Slime_ร่างแข็ง) plays clearly, then respawn at the start point.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(PlayerController2D), typeof(PlayerStress))]
public class PlayerRespawn : MonoBehaviour
{
    [Tooltip("Seconds between death and respawn (0.75s lets the meltdown / crystal shatter animation play completely).")]
    [SerializeField] private float respawnDelay = 0.75f;

    [Tooltip("Downward slide speed during Meltdown Overload death if dying on a wall / in the air.")]
    [SerializeField] private float meltdownSlideDownSpeed = 3.5f;

    [Tooltip("Optional. If empty, the position the player has at Start() is used.")]
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
        if (respawnDelay < 0.7f) respawnDelay = 0.75f;
    }

    private void Start() => startPosition = transform.position;

    private void OnEnable()
    {
        if (stress != null) stress.Overloaded += OnOverloaded;
    }

    private void OnDisable()
    {
        if (stress != null) stress.Overloaded -= OnOverloaded;
    }

    private void OnOverloaded() => Kill();

    /// <summary>Kills the player and starts the respawn sequence. Safe to call repeatedly.</summary>
    public void Kill()
    {
        if (IsDead) return;
        IsDead = true;

        controller.SetControlEnabled(false);
        Died?.Invoke();
        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        yield return null;

        bool isMeltdownDeath = stress != null && stress.Current >= stress.CriticalThreshold;
        if (isMeltdownDeath)
        {
            rb.simulated = true;
            rb.gravityScale = 0f;
            float elapsed = 0f;
            while (elapsed < respawnDelay)
            {
                rb.linearVelocity = new Vector2(0f, -meltdownSlideDownSpeed);
                elapsed += Time.deltaTime;
                yield return null;
            }
            rb.linearVelocity = Vector2.zero;
        }
        else
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
            yield return new WaitForSeconds(respawnDelay);
        }

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
