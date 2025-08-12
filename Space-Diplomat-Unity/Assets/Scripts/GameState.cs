using System;
using System.Collections.Generic;
using UnityEngine;

public class GameState : MonoBehaviour
{
    public static GameState Instance { get; private set; }
    public static event Action<string> EmotionsChanged;

    public string sessionId {  get; private set; }
    public bool serverAffectResetDone { get; private set; } = false;

    // A record per alien
    public class AlienData
    {
        public string lastEmotionKey = "Neutral"; // Default emotion key
        public List<string> chatHistory = new List<string>(); // Chat history for the alien
        public Dictionary<string, float> emotionCounts = new Dictionary<string, float>()
        {{"joy", 0 }, {"sadness", 0 }, {"anger", 0 },
            {"disgust", 0}, {"fear", 0} }; // Emotion counts for the alien
        public bool conversationClosed; // Flag to indicate if the conversation is closed
        public string conclusionMessage; // Conclusion message for the alien conversation
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

        // New run -> New session
        sessionId = System.Guid.NewGuid().ToString("N");
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

    public void MarkServerAffectReset()
    {
        serverAffectResetDone = true;
    }

    public void RaiseEmotionsChanged(string alienName = null)
    {
        EmotionsChanged?.Invoke(alienName);
    }
}
