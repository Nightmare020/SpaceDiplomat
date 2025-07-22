using UnityEngine;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    public static AudioManager audioInstance; // Static instance for singleton pattern

    public AudioSource backgroundMusic; // Reference to the background music AudioSource
    public AudioClip musicClip; // Reference to the background music AudioClip

    // Awake is called when the script instance is being loaded
    private void Awake()
    {
        // Check if an instance already exists
        if (audioInstance == null)
        {
            audioInstance = this; // Set this instance as the singleton instance
            DontDestroyOnLoad(gameObject); // Prevent this object from being destroyed on scene load
            InitAudio(); // Initialize audio settings
        }
        else
        {
            Destroy(gameObject); // Destroy this instance if another one already exists
        }
    }

    
    private void InitAudio()
    {
        backgroundMusic = gameObject.AddComponent<AudioSource>(); // Add an AudioSource component to the game object
        backgroundMusic.clip = musicClip; // Assign the music clip to the AudioSource
        backgroundMusic.loop = true; // Set the AudioSource to loop the music
        backgroundMusic.playOnAwake = true; // Play the music when the scene starts
        backgroundMusic.Play(); // Start playing the background music
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
