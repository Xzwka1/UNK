using UnityEngine;
using Random = UnityEngine.Random;

public class SoundManager : MonoBehaviour
{
    public static SoundManager instance;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource bgmSource;

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    /// <summary>Plays a one-shot SFX on the shared SFX source. The source itself is never destroyed.</summary>
    public void PlaySound(AudioClip sound, float volume)
    {
        if (sound == null || sfxSource == null) return;
        sfxSource.PlayOneShot(sound, volume);
    }

    /// <summary>Plays one random clip from the array as a one-shot SFX.</summary>
    public void RandomPlaySound(AudioClip[] sound, float volume)
    {
        if (sound == null || sound.Length == 0 || sfxSource == null) return;
        AudioClip clip = sound[Random.Range(0, sound.Length)];
        if (clip == null) return;
        sfxSource.PlayOneShot(clip, volume);
    }

    /// <summary>Starts looping background music on the dedicated BGM source.</summary>
    public void PlayBGM(AudioClip sound, float volume)
    {
        if (sound == null || bgmSource == null) return;
        bgmSource.clip = sound;
        bgmSource.volume = volume;
        bgmSource.loop = true;
        bgmSource.Play();
    }

    public void StopBGM()
    {
        if (bgmSource != null) bgmSource.Stop();
    }
}
