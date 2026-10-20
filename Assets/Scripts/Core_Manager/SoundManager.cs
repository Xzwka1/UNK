using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class SoundManager : MonoBehaviour
{
    public static SoundManager instance;
    [SerializeField] private AudioClip sfx;
    [SerializeField] private AudioClip[] randomsfx;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private float volume;

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    public void PlaySound(AudioClip sound, float volume)
    {
        if (sound == null || sfxSource == null) return;
        sfx = sound;
        sfxSource.volume = volume;
        float clipLenght = sfxSource.clip.length;
        sfxSource.PlayOneShot(sfx);
        Destroy(sfxSource, clipLenght);
    }
    public void RandomPlaySound(AudioClip[] sound, float volume)
    {
        if (sound == null || sfxSource == null) return;
        randomsfx = sound;
        sfxSource.volume = volume;
        float clipLenght = sfxSource.clip.length;
        int random = Random.Range(0, sound.Length);
        sfxSource.PlayOneShot(randomsfx[random]);
        Destroy(sfxSource, clipLenght);

    }
    public void PlayBGM(AudioClip sound, float volume)
    {
        sfxSource.clip = sound;
        sfxSource.volume = volume;
        sfxSource.loop = true; 
        sfxSource.Play();
    }
    public void StopBGM()
    {
        if (bgmSource != null) bgmSource.Stop();
    }

}
