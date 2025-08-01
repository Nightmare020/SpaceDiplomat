using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    public static AudioManager audioInstance; // Static instance for singleton pattern

    [Header("Clips")]
    public AudioClip musicClip; // Reference to the background music AudioClip

    [Header("Mixer")]
    [SerializeField] private AudioMixer audioMixer; // Reference to the AudioMixer for volume control
    [SerializeField] private string mixerVolumeParam = "MusicVolume"; // Parameter name in the AudioMixer for volume control
    [SerializeField] private string musicGroupPath = "Music"; // Audio group path in the AudioMixer

    private AudioSource backgroundMusic; // AudioSource for playing the background music

    // Awake is called when the script instance is being loaded
    private void Awake()
    {
        if (audioInstance != null)
        {
            Destroy(gameObject); // Destroy this instance if another one already exists
            return; // Exit the method to prevent further execution
        }

        audioInstance = this; // Set this instance as the singleton instance
        DontDestroyOnLoad(gameObject); // Prevent this object from being destroyed on scene load

        InitAudio(); // Initialize audio settings
    }


    private void InitAudio()
    {
        backgroundMusic = gameObject.AddComponent<AudioSource>(); // Add an AudioSource component to the game object
        backgroundMusic.clip = musicClip; // Assign the music clip to the AudioSource
        backgroundMusic.loop = true; // Set the AudioSource to loop the music
        backgroundMusic.playOnAwake = true; // Play the music when the scene starts

        if (audioMixer != null)
        {
            var groups = audioMixer.FindMatchingGroups(musicGroupPath); // Find matching groups in the AudioMixer
            if (groups.Length > 0)
            {
                backgroundMusic.outputAudioMixerGroup = groups[0]; // Assign the first matching group to the AudioSource
            }
        }

        backgroundMusic.Play(); // Start playing the background music
    }

    // ------------ Utility API used by pause menu controller class ------------
    public void SetNormalizedMusicVolume(float t01) // t01 = 0..1
    {
        t01 = Mathf.Clamp01(t01); // Ensure the value is between 0 and 1

        if (audioMixer != null && mixerVolumeParam != "")
        {
            float dB = Mathf.Lerp(-30f, 0f, t01); // Map 0..1 to -30dB..0dB
            audioMixer.SetFloat(mixerVolumeParam, dB); // Set the volume in the AudioMixer
        }
        else
        {
            AudioListener.volume = t01; // Set the volume directly on the AudioListener if no AudioMixer is set
        }
    }

    public float GetNormalizedMusicVolume() // Returns 0..1
    {
        if (audioMixer != null && mixerVolumeParam != "" &&
            audioMixer.GetFloat(mixerVolumeParam, out var dB))
        {
            return Mathf.InverseLerp(-30f, 0f, dB); // Map -30dB..0dB to 0..1
        }

        return AudioListener.volume; // Return the volume directly from the AudioListener
    }

    public float GetPlaybackTime()
    {
        return backgroundMusic.time; // Return the current playback time of the background music
    }

    public void SetPlaybackTime(float time)
    {
        backgroundMusic.time = time; // Set the playback time of the background music

        if (!backgroundMusic.isPlaying)
        {
            backgroundMusic.Play(); // Play the music if it is not already playing
        }
    }
}
