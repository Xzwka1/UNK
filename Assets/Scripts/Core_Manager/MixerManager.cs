using UnityEngine;
using UnityEngine.Audio;

public class MixerManager : MonoBehaviour
{
    [SerializeField] private AudioMixer audioMixer;

    public void SetMasterVolume(float level)
    {
        audioMixer.SetFloat("MasterVolume", Mathf.Log10(level) * 20);
    }
    public void SetSFXVolume(float level)
    {
        audioMixer.SetFloat("SFXVolume", Mathf.Log10(level) * 20);
    }
    public void SetMusicVolume(float level)
    {
        audioMixer.SetFloat("MusicVolume", Mathf.Log10(level) * 20);
    }
}
