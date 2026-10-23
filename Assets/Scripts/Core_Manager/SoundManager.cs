using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Central Sound System (AudioManager / SoundManager) for "The Fragile Equilibrium".
/// Handles BGM, SFX, pitch variation, volume controls, and links to all game events.
/// Persists across scenes with DontDestroyOnLoad.
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager instance;
    public static SoundManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<SoundManager>();
                if (instance == null)
                {
                    // Try loading from Resources prefab first
                    GameObject prefab = Resources.Load<GameObject>("SoundManager");
                    GameObject go;
                    if (prefab != null)
                    {
                        go = Instantiate(prefab);
                    }
                    else
                    {
                        go = new GameObject("SoundManager");
                        instance = go.AddComponent<SoundManager>();
                    }
                    go.name = "SoundManager (Persistent)";
                    DontDestroyOnLoad(go);
                }
            }
            return instance;
        }
    }

    [Header("Audio Sources")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource loopSource; // for continuous sounds like wall slide

    [Header("Volume Settings")]
    [Range(0f, 1f)] [SerializeField] private float masterVolume = 1f;
    [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1f;
    [Range(0f, 1f)] [SerializeField] private float bgmVolume = 0.8f;

    [Header("Pitch Variation (makes repeated SFX sound natural)")]
    [SerializeField] private bool usePitchVariation = true;
    [SerializeField] private float minPitch = 0.95f;
    [SerializeField] private float maxPitch = 1.05f;

    [Header("Player Movement SFX")]
    [SerializeField] private AudioClip jumpClip;
    [SerializeField] private AudioClip doubleJumpClip;
    [SerializeField] private AudioClip dashClip;
    [SerializeField] private AudioClip dashEmergencyClip;
    [SerializeField] private AudioClip wallImpactClip;
    [SerializeField] private AudioClip wallSlideClip;
    [SerializeField] private AudioClip wallTouchClip;
    [SerializeField] private AudioClip landClip;
    [SerializeField] private AudioClip walkClip;
    [SerializeField] private AudioClip respawnClip;

    [Header("Stress & Polarity SFX")]
    [SerializeField] private AudioClip polaritySnapClip;
    [SerializeField] private AudioClip liquidLockoutClip;
    [SerializeField] private AudioClip brittleLockoutClip;
    [SerializeField] private AudioClip bodyCrackClip;
    [SerializeField] private AudioClip meltDeathClip;
    [SerializeField] private AudioClip crystalDeathClip;

    [Header("Environment SFX")]
    [SerializeField] private AudioClip platformCrackClip;
    [SerializeField] private AudioClip platformBreakClip;
    [SerializeField] private AudioClip barrierBreakClip;

    [Header("UI & Music")]
    [SerializeField] private AudioClip buttonClickClip;
    [SerializeField] private AudioClip mainMenuBGM;
    [SerializeField] private AudioClip stageClearClip;
    [SerializeField] private AudioClip gameplayBGM;

    // Properties for volume control
    public float MasterVolume
    {
        get => masterVolume;
        set { masterVolume = Mathf.Clamp01(value); UpdateSourceVolumes(); }
    }
    public float SfxVolume
    {
        get => sfxVolume;
        set { sfxVolume = Mathf.Clamp01(value); UpdateSourceVolumes(); }
    }
    public float BgmVolume
    {
        get => bgmVolume;
        set { bgmVolume = Mathf.Clamp01(value); UpdateSourceVolumes(); }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureAudioSources();
    }

    private void EnsureAudioSources()
    {
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }
        if (bgmSource == null)
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.playOnAwake = false;
            bgmSource.loop = true;
        }
        if (loopSource == null)
        {
            loopSource = gameObject.AddComponent<AudioSource>();
            loopSource.playOnAwake = false;
            loopSource.loop = true;
        }

        UpdateSourceVolumes();
    }

    private void UpdateSourceVolumes()
    {
        if (sfxSource != null) sfxSource.volume = masterVolume * sfxVolume;
        if (bgmSource != null) bgmSource.volume = masterVolume * bgmVolume;
        if (loopSource != null) loopSource.volume = masterVolume * sfxVolume;
    }

    // ------------------------------------------------------------------ Core Playback
    public void PlaySound(AudioClip sound, float volume = 1f)
    {
        PlaySound(sound, volume, false);
    }

    public void PlaySound(AudioClip sound, float volume, bool varyPitch)
    {
        if (sound == null) return;
        EnsureAudioSources();

        if (varyPitch && usePitchVariation)
        {
            sfxSource.pitch = Random.Range(minPitch, maxPitch);
        }
        else
        {
            sfxSource.pitch = 1f;
        }

        sfxSource.PlayOneShot(sound, volume * masterVolume * sfxVolume);
    }

    public void RandomPlaySound(AudioClip[] sound, float volume = 1f)
    {
        if (sound == null || sound.Length == 0) return;
        AudioClip clip = sound[Random.Range(0, sound.Length)];
        if (clip != null) PlaySound(clip, volume, true);
    }

    public void PlayBGM(AudioClip sound, float volume = 1f)
    {
        if (sound == null) return;
        EnsureAudioSources();

        if (bgmSource.clip == sound && bgmSource.isPlaying) return;

        bgmSource.clip = sound;
        bgmSource.volume = volume * masterVolume * bgmVolume;
        bgmSource.loop = true;
        bgmSource.Play();
    }

    public void StopBGM()
    {
        if (bgmSource != null) bgmSource.Stop();
    }

    public void PlayLoopingSFX(AudioClip sound, bool play, float volume = 1f)
    {
        EnsureAudioSources();
        if (play)
        {
            if (loopSource.clip != sound || !loopSource.isPlaying)
            {
                loopSource.clip = sound;
                loopSource.volume = volume * masterVolume * sfxVolume;
                loopSource.Play();
            }
        }
        else
        {
            if (loopSource.isPlaying) loopSource.Stop();
        }
    }

    // ------------------------------------------------------------------ Static Global API
    public static void PlayJump() => Instance?.PlaySound(Instance.jumpClip, 0.9f, true);
    public static void PlayDoubleJump() => Instance?.PlaySound(Instance.doubleJumpClip, 1f, true);
    public static void PlayDash(bool emergency = false)
    {
        if (Instance == null) return;
        AudioClip clip = emergency && Instance.dashEmergencyClip != null ? Instance.dashEmergencyClip : Instance.dashClip;
        Instance.PlaySound(clip, 1f, true);
    }
    public static void PlayWallImpact() => Instance?.PlaySound(Instance.wallImpactClip, 1f, true);
    public static void PlayWallTouch() => Instance?.PlaySound(Instance.wallTouchClip, 0.8f, true);
    public static void PlayLand() => Instance?.PlaySound(Instance.landClip, 0.85f, true);
    public static void PlayWallSlide(bool isSliding) => Instance?.PlayLoopingSFX(Instance.wallSlideClip, isSliding, 0.7f);
    public static void PlayFootstep() => Instance?.PlaySound(Instance.walkClip, 0.5f, true);
    public static void PlayRespawn() => Instance?.PlaySound(Instance.respawnClip, 1f, false);

    public static void PlayPolaritySnap() => Instance?.PlaySound(Instance.polaritySnapClip, 1f, false);
    public static void PlayLockoutDenied(bool isLiquid)
    {
        if (Instance == null) return;
        AudioClip clip = isLiquid ? Instance.liquidLockoutClip : Instance.brittleLockoutClip;
        Instance.PlaySound(clip, 1f, false);
    }
    public static void PlayBodyCrack() => Instance?.PlaySound(Instance.bodyCrackClip, 0.9f, true);
    public static void PlayDeath(bool isCrystal)
    {
        if (Instance == null) return;
        AudioClip clip = isCrystal && Instance.crystalDeathClip != null ? Instance.crystalDeathClip : Instance.meltDeathClip;
        Instance.PlaySound(clip, 1f, false);
    }

    public static void PlayPlatformCrack() => Instance?.PlaySound(Instance.platformCrackClip, 0.9f, true);
    public static void PlayPlatformBreak() => Instance?.PlaySound(Instance.platformBreakClip, 1f, false);
    public static void PlayBarrierBreak() => Instance?.PlaySound(Instance.barrierBreakClip, 1f, false);

    public static void PlayButtonClick() => Instance?.PlaySound(Instance.buttonClickClip, 1f, false);
    public static void PlayStageClear() => Instance?.PlaySound(Instance.stageClearClip, 1f, false);
    public static void PlayMainMenuMusic()
    {
        if (Instance != null && Instance.mainMenuBGM != null)
            Instance.PlayBGM(Instance.mainMenuBGM, 0.8f);
    }
    public static void PlayGameplayMusic()
    {
        if (Instance != null && Instance.gameplayBGM != null)
            Instance.PlayBGM(Instance.gameplayBGM, 0.75f);
    }

#if UNITY_EDITOR
    private void Reset()
    {
        AutoAssignClips();
    }

    [ContextMenu("Auto-Assign Audio Clips From Project")]
    public void AutoAssignClips()
    {
        // Player Movement
        jumpClip = LoadClip("Assets/Sound/Week-1/Slime-Jump.mp3");
        doubleJumpClip = LoadClip("Assets/Sound/Week-1/Slime-Double-Jump.mp3");
        dashClip = LoadClip("Assets/Sound/Week-1/Slime-Dash.mp3");
        dashEmergencyClip = LoadClip("Assets/Sound/Week-2/Dash ละลายแต้มติดลบ.mp3");
        wallImpactClip = LoadClip("Assets/Sound/Week-1/Slime-Dash-ตัวละครกระแทกกำแพง.mp3");
        wallSlideClip = LoadClip("Assets/Sound/Week-1/Slime-สไลด์.mp3");
        landClip = LoadClip("Assets/Sound/Week-1/Slime-ตกพื้น.mp3");
        walkClip = LoadClip("Assets/Sound/Week-1/slime-Walking.mp3");
        wallTouchClip = LoadClip("Assets/Sound/Week-1/Slime-ตัวละครโดยกำแพง.mp3");
        respawnClip = LoadClip("Assets/Sound/Week-1/Slime-Respawn.mp3");

        // Stress & Polarity
        polaritySnapClip = LoadClip("Assets/Sound/Week-2/ตอนสลับขั่ว.mp3");
        liquidLockoutClip = LoadClip("Assets/Sound/Week-2/เสียงตอนละลายกดท่าไม่ติด.mp3");
        brittleLockoutClip = LoadClip("Assets/Sound/Week-2/กลางเป็นของแข็งกดท่าไม่ติด.mp3");
        bodyCrackClip = LoadClip("Assets/Sound/Week-1/Slime-ตัวร้าว.mp3");
        meltDeathClip = LoadClip("Assets/Sound/Week-1/Slime-Death.mp3");
        crystalDeathClip = LoadClip("Assets/Sound/Week-2/ตายแบบแก้วแตก.mp3");

        // Environment
        platformCrackClip = LoadClip("Assets/Sound/Week-1/Slime-พื้นร้าว.mp3");
        platformBreakClip = LoadClip("Assets/Sound/Week-2/พื้นพัง.mp3");
        barrierBreakClip = LoadClip("Assets/Sound/Week-1/Impact-Wall.mp3");

        // UI & BGM
        buttonClickClip = LoadClip("Assets/Sound/Week-1/Button-Menu.mp3");
        mainMenuBGM = LoadClip("Assets/Sound/Week-2/เสียงตอน Main Menu.mp3");
        stageClearClip = LoadClip("Assets/Sound/Week-2/เพลงตอนจบ.mp3");
        gameplayBGM = LoadClip("Assets/Sound/Music/The_Slime_s_Morning_Route.mp3");

        EnsureAudioSources();
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("[SoundManager] All 21 audio clips auto-assigned successfully!");
    }

    private AudioClip LoadClip(string path)
    {
        return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
#endif
}
