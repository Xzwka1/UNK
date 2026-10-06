using System.Collections;
using UnityEngine;

/// <summary>
/// Screen Shake + Freeze Frame (hit-stop) manager. Put it on the same GameObject as the camera.
///
/// It never moves the camera itself: it only computes <see cref="CurrentOffset"/> every frame, and
/// CameraController2D adds that offset after its follow logic, so shake never fights with the smooth damping.
///
/// Everything runs on UNSCALED time, so shakes keep playing and decaying while Time.timeScale is 0 during a freeze.
///
/// Wiring: assign the Player's PlayerRespawn (auto-found if empty). When the player dies - whether from a hazard or from
/// Stress reaching 100 % (PlayerStress.Overloaded -> PlayerRespawn.Kill -> Died) - a Heavy Shake and a Freeze Frame
/// start on the same frame. Subscribing only to Died avoids double-firing for the Overload case.
///
/// Other scripts (breakable wall, dash code, ...) call:
///   CameraEffects.Instance.MicroShake();  CameraEffects.Instance.HeavyShake();  CameraEffects.Instance.FreezeFrame(0.05f);
/// </summary>
[DefaultExecutionOrder(-50)]
public class CameraEffects : MonoBehaviour
{
    [System.Serializable]
    public class ShakeProfile
    {
        [Tooltip("Maximum offset in world units at the start of the shake.")]
        [Min(0f)] public float amplitude = 0.1f;
        [Tooltip("Length in seconds (unscaled).")]
        [Min(0.01f)] public float duration = 0.07f;
        [Tooltip("How many times per second the shake direction changes. Use ~60+ for a violent rattle.")]
        [Min(1f)] public float frequency = 60f;
        [Tooltip("Strength over the shake's lifetime: X = 0..1 progress, Y = 0..1 multiplier.")]
        public AnimationCurve falloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);
    }

    public static CameraEffects Instance { get; private set; }

    [Header("Micro Shake (dash into breakable wall, heavy landing on fragile platform)")]
    [SerializeField]
    private ShakeProfile microShake = new ShakeProfile { amplitude = 0.08f, duration = 0.07f, frequency = 60f };

    [Header("Heavy Shake (Stress 100 % / death)")]
    [SerializeField]
    private ShakeProfile heavyShake = new ShakeProfile { amplitude = 0.5f, duration = 0.35f, frequency = 45f };

    [Header("Freeze Frame (Hit Stop)")]
    [Tooltip("Real-time seconds Time.timeScale stays at 0 when the player dies.")]
    [SerializeField, Min(0f)] private float deathFreezeDuration = 0.05f;

    [Header("Death hookup")]
    [Tooltip("Auto-found if empty.")]
    [SerializeField] private PlayerRespawn player;
    [SerializeField] private bool reactToPlayerDeath = true;

    /// <summary>Shake offset to add to the camera position this frame (world units).</summary>
    public Vector2 CurrentOffset { get; private set; }
    public bool IsFrozen => frozen;

    // Shake runtime
    private ShakeProfile active;
    private float activeAmplitude;
    private float elapsed;
    private float sampleTimer;
    private Vector2 sample;

    // Freeze runtime
    private bool frozen;
    private float cachedTimeScale = 1f;
    private float freezeEndRealtime;

    // ------------------------------------------------------------------ Lifecycle
    private void Awake()
    {
        Instance = this;
        if (player == null) player = FindFirstObjectByType<PlayerRespawn>();
    }

    private void OnEnable()
    {
        Instance = this;
        if (player != null) player.Died += OnPlayerDied;
    }

    private void OnDisable()
    {
        if (player != null) player.Died -= OnPlayerDied;
        RestoreTimeScale();                  // never leave the game frozen
        CurrentOffset = Vector2.zero;
        active = null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (active == null)
        {
            CurrentOffset = Vector2.zero;
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= active.duration)
        {
            active = null;
            CurrentOffset = Vector2.zero;
            return;
        }

        // New random direction at the profile's frequency; strength follows the falloff curve.
        sampleTimer -= Time.unscaledDeltaTime;
        if (sampleTimer <= 0f)
        {
            sampleTimer = 1f / active.frequency;
            sample = Random.insideUnitCircle;
        }

        float envelope = Mathf.Clamp01(active.falloff.Evaluate(elapsed / active.duration));
        CurrentOffset = sample * (activeAmplitude * envelope);
    }

    // ------------------------------------------------------------------ Shake API
    public void MicroShake() => Shake(microShake);
    public void HeavyShake() => Shake(heavyShake);

    /// <summary>
    /// Starts a shake. If one is already playing, the stronger of the two (current remaining strength vs the new one)
    /// wins, so a small shake can never cut a heavy one short.
    /// </summary>
    public void Shake(ShakeProfile profile)
    {
        if (profile == null) return;

        if (active != null)
        {
            float remaining = activeAmplitude * Mathf.Clamp01(active.falloff.Evaluate(elapsed / active.duration));
            if (profile.amplitude < remaining) return;
        }

        active = profile;
        activeAmplitude = profile.amplitude;
        elapsed = 0f;
        sampleTimer = 0f;                    // pick a direction on the first frame
    }

    // ------------------------------------------------------------------ Freeze Frame API
    /// <summary>
    /// Sets Time.timeScale to 0 for <paramref name="duration"/> real seconds, then restores the previous value.
    /// Overlapping calls extend the freeze instead of stacking, and the original time scale is always the one restored.
    /// The end is checked once per frame, so the real length is the duration rounded up to the next frame.
    /// </summary>
    public void FreezeFrame(float duration)
    {
        if (duration <= 0f) return;

        float end = Time.unscaledTime + duration;
        if (frozen)
        {
            freezeEndRealtime = Mathf.Max(freezeEndRealtime, end);
            return;
        }

        cachedTimeScale = Time.timeScale;
        frozen = true;
        freezeEndRealtime = end;
        Time.timeScale = 0f;
        StartCoroutine(FreezeRoutine());
    }

    /// <summary>Heavy Shake + Freeze Frame on the same frame (the death effect).</summary>
    public void HeavyShakeWithFreeze()
    {
        HeavyShake();
        FreezeFrame(deathFreezeDuration);
    }

    private IEnumerator FreezeRoutine()
    {
        while (Time.unscaledTime < freezeEndRealtime) yield return null;
        RestoreTimeScale();
    }

    private void RestoreTimeScale()
    {
        if (!frozen) return;
        frozen = false;
        Time.timeScale = cachedTimeScale;
    }

    // ------------------------------------------------------------------ Hooks
    private void OnPlayerDied()
    {
        if (reactToPlayerDeath) HeavyShakeWithFreeze();
    }
}
