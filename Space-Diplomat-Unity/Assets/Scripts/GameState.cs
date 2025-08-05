using System.Collections.Generic;
using UnityEngine;

public class GameState : MonoBehaviour
{
    public static GameState Instance { get; private set; }

    // A record per alien
    public class AlienData
    {
        public string lastEmotionKey = "Neutral"; // Default emotion key
        public List<string> chatHistory = new List<string>(); // Chat history for the alien
        public Dictionary<string, float> emotionCounts = new Dictionary<string, float>()
        {{"joy", 0 }, {"sadness", 0 }, {"anger", 0 },
            {"surprise", 0}, {"fear", 0} }; // Emotion counts for the alien
    }

    public Dictionary<string, AlienData> aliensData = new Dictionary<string, AlienData>(); // Dictionary to hold alien data

    // Awake is called when the script instance is being loaded
    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject); // Ensure only one instance exists
            return;
        }

        Instance = this; // Set the singleton instance
        DontDestroyOnLoad(gameObject); // Persist this object across scenes
    }

    // Convenience
    public AlienData GetAlienData(string alienName)
    {
        if (!aliensData.ContainsKey(alienName))
        {
            aliensData[alienName] = new AlienData(); // Create new data if it doesn't exist
        }
        return aliensData[alienName];
    }
}
