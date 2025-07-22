using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChanger : MonoBehaviour
{
    public static SceneChanger instance; // Static instance for singleton pattern

    private float savedTime;

    // Awake is called when the script instance is being loaded
    private void Awake()
    {
        // Check if an instance already exists
        if (instance == null)
        {
            instance = this; // Set this instance as the singleton instance
            DontDestroyOnLoad(gameObject); // Prevent this object from being destroyed on scene load
        }
        else
        {
            Destroy(gameObject); // Destroy this instance if another one already exists
        }
    }

    public void ChangeScene(string sceneName)
    {
        // Save the current playback time of the background music
        savedTime = AudioManager.audioInstance.GetPlaybackTime();

        // Subscribe to the scene loaded event
        SceneManager.sceneLoaded += OnSceneLoaded;

        // Load the new scene
        SceneManager.LoadScene(sceneName);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Set the playback time of the background music to the saved time
        AudioManager.audioInstance.SetPlaybackTime(savedTime);

        // Unsubscribe from the scene loaded event to avoid memory leaks
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}
