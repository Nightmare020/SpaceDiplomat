using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Linq;
using UnityEngine.SceneManagement;
using Unity.VisualScripting;

public class ChatManager : MonoBehaviour
{
    private string currentAlienName; // Name of the alien character

    public TMP_InputField inputField; // Input field for user messages
    public TMP_Text conversationText; // Text for message displaying
    public Transform contentArea; // Parent object for messages in the UI
    public ScrollRect scrollRect; // ScrollRect to manage scrolling

    public Image fillBar; // Fill bar for visual max char feedback
    public int maxChars = 300; // Maximum number of words allowed in a message
    public Image alienEmotionImage; // Image to display alien emotion

    private const string API_URL = "http://127.0.0.1:5000/chat";
    private bool alienTalking = false;
    private bool _chatLocked = false;
    private string _pendingSystemLine = null; // Queued end notice printed after typing finishes
    private float _joyThresholdCache = 0.9f; // Threshold for joy emotion to consider negotiation success
    private float _angerToleranceCache = 0.9f; // Threshold for anger emotion to consider negotiation failure
    private GameState.AlienData alienData => 
        string.IsNullOrEmpty(currentAlienName) ? null : GameState.Instance.GetAlienData(currentAlienName); // Get the alien data for the current alien

    // Awake is called when the script instance is being loaded
    void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        currentAlienName = PlayerData.SelectedAlienName; // Get the selected alien name from PlayerData
        
        if (string.IsNullOrEmpty(currentAlienName))
        {
            // No planet selected -> Hide the image completely
            alienEmotionImage.enabled = false;
            return;
        }
        else
        {
            alienEmotionImage.enabled = true; // Show the image if a planet is selected
            SetInitialAlienSprite(); // Set the initial alien emotion sprite
            RestoreHistory(); // Restore chat history for the selected alien
            EnforceConclusionGateIfAny(); // Enforce conclusion gate if the conversation is closed
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        inputField.lineType = TMP_InputField.LineType.SingleLine; // Set input field to single line mode

        // Use TMP's submit
        inputField.onSubmit.AddListener((message) => SubmitMessage()); // Add listener for input field submission

        // Ensure enabled initially
        inputField.interactable = true;
    }

    // Update is called once per frame
    void Update()
    {
        UpdateCharacterCountBar();
    }

    // Call once when the scene opens
    private void RestoreHistory()
    {
        var sb = new System.Text.StringBuilder(alienData.chatHistory.Count * 32);
        foreach (string line in alienData.chatHistory)
        {
            sb.AppendLine(line); // Display each line from the chat history
        }

        conversationText.text = sb.ToString();

        // Let the layout rebuild this frame, then snap to bottom
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)conversationText.transform);
        StartCoroutine(ScrollToBottomNextFrame());
    }

    // Call every time player or alien speaks
    private void RecordLine(string sender, string message)
    {
        string line = $"{sender}: {message}"; // Format the line with sender and message
        alienData.chatHistory.Add(line); // Add the line to the chat history
    }

    private void SetInitialAlienSprite()
    {
        // Grab whatever emotion we left this alien in
        string lastEmotionKey = alienData.lastEmotionKey;

        if (string.IsNullOrEmpty(lastEmotionKey))
        {
            lastEmotionKey = "Neutral"; // Default to Neutral if no emotion is set
        }

        // Load the initial alien emotion sprite based on the selected alien name
        alienEmotionImage.sprite = LoadEmotion(currentAlienName, lastEmotionKey);
    }

    void SubmitMessage()
    {
        // Don't allow sending if conversation is closed
        if (alienData != null && alienData.conversationClosed)
        {
            // Avoid duplicate lines
            string requiredLine = $"SYSTEM: {alienData.conclusionMessage}";
            if (alienData.chatHistory.Count == 0 || alienData.chatHistory[^1] != requiredLine)
            {
                DisplayMessage("SYSTEM", alienData.conclusionMessage); // Display the conclusion message
            }

            inputField.interactable = false; // Disable input field interaction
            return;
        }

        // Block whule alien is sending message
        if (alienTalking) return;

        // Capture and clear immediately so nothing lingers in the input box
        string message = inputField.text.Trim();
        inputField.text = ""; // Clear the input field immediately
        inputField.caretPosition = 0; // Reset caret position to the start
        inputField.selectionStringAnchorPosition = 0; // Reset selection anchor position to the start
        inputField.selectionStringFocusPosition = 0; // Reset selection focus position to the start
        inputField.ActivateInputField(); // Reactivate the input field for new input

        if (string.IsNullOrEmpty(currentAlienName))
        {
            DisplayMessage("SYSTEM", "You haven't started any alien communication yet.");
            return; // Exit if no alien is selected
        }

        if (!string.IsNullOrEmpty(message))
        {
            DisplayMessage("YOU", message);
            StartCoroutine(SendMessageToGroq(message));
            inputField.interactable = false; // Lock immediately after sending
        }
    }

    void UpdateCharacterCountBar()
    {
        int charCount = inputField.text.Length;
        float percent = Mathf.Clamp01((float)charCount / maxChars);

        // Fill the bar
        fillBar.fillAmount = percent;

        // Set color logic
        if (charCount == 0)
            fillBar.color = Color.white; // No input
        else if (charCount < maxChars)
            fillBar.color = Color.yellow; // Safe zone
        else
        {
            fillBar.color = Color.red; // At or over limit

            // Trim if exceeding limit
            inputField.text = inputField.text.Substring(0, maxChars);
            inputField.caretPosition = inputField.text.Length; // Move caret to end
        }
    }

    void DisplayMessage(string sender, string message)
    {
        if (!string.IsNullOrEmpty(currentAlienName) && alienData != null)
        {
            RecordLine(sender, message); // Record the line in the chat history
        }

        bool isAlien = sender != "YOU" && sender != "SYSTEM"; // Check if the sender is an alien

        if (isAlien)
            StartCoroutine(TypeText($"\n> {sender}: {message}\n\n"));
        else
        {
            string formattedMessage = $"> {sender}: {message}\n";
            conversationText.text += formattedMessage;

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)conversationText.transform);
            Canvas.ForceUpdateCanvases(); // Update the canvas to ensure the new message is visible
            StartCoroutine(ScrollToBottomNextFrame()); // Scroll to the bottom
        }
    }

    IEnumerator SendMessageToGroq(string playerInput)
    {
        // Build a typed payload so JsonUtility makes correct JSON
        var payload = new ChatRequest { 
            message = playerInput, 
            alienName = currentAlienName,
            history = BuildHistoryTurns(15) // Build the chat history for the alien
        };

        string json = JsonUtility.ToJson(payload);

        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
        UnityWebRequest request = new UnityWebRequest(API_URL, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            ChatResponse response = JsonUtility.FromJson<ChatResponse>(request.downloadHandler.text);

            // Cache threshold from server, if present
            if (response.alienProfile != null && 
                (response.alienProfile.joyThreshold > 0f && response.alienProfile.joyThreshold <= 1f) &&
                (response.alienProfile.angerTolerance > 0f && response.alienProfile.angerTolerance <= 1f))
            {
                _joyThresholdCache = response.alienProfile.joyThreshold; // Cache joy threshold for negotiation success
                _angerToleranceCache = response.alienProfile.angerTolerance; // Cache anger tolerance for negotiation failure
            }

            alienTalking = true;
            DisplayMessage(currentAlienName, response.reply);

            // Update counts using full vector
            if (!string.IsNullOrEmpty(response.analysis.distributionJson))
            {
                var wrapper = JsonUtility.FromJson<DistributionWrapper>(response.analysis.distributionJson);

                // Reset the five cannonical buckets
                foreach (var key in new[] { "joy", "sadness", "anger", "fear", "disgust" })
                    alienData.emotionCounts[key] = 0f;

                for (int i = 0; i < wrapper.keys.Length; i++)
                {
                    string key = (wrapper.keys[i] ?? "").ToLower(); // Normalize the key to lowercase
                    float value = Mathf.Max(0f, wrapper.values[i]); // Ensure the value is non-negative

                    // Keep only the five canonical emotions
                    if (key is "joy" or "sadness" or "anger" or "fear" or "disgust")
                    {
                        alienData.emotionCounts[key] = value; // Increment the count for the emotion
                    }
                }

                // Re-draw the donut so the UI reflects the new distribution
                DonutBuilder donut = FindFirstObjectByType<DonutBuilder>();
                if (donut != null)
                {
                    donut.Refresh();
                }
            }

            if (response.analysis != null)
            {
                UpdateAlienEmotion(response.analysis.emotion, response.analysis.emotionScore);

                // -------- GOAP Hard Stop --------
                if (response.negotiationSuccess || GoapGate.IsSuccess(alienData.emotionCounts["joy"]))
                {
                    _pendingSystemLine = "Diplomatic solution reached. Talks Concluded.";

                    // Persist closed state
                    alienData.conversationClosed = true; // Mark the conversation as closed
                    alienData.conclusionMessage = _pendingSystemLine; // Store the conclusion message
                }

                if (response.negotiationFailure || GoapGate.IsFailure(alienData.emotionCounts["anger"]))
                {
                    _pendingSystemLine = "Negotiation failed. The alien refuses to continue.";

                    // Persist closed state
                    alienData.conversationClosed = true; // Mark the conversation as closed
                    alienData.conclusionMessage = _pendingSystemLine; // Store the conclusion message
                }
            }
        }
        else
        {
            DisplayMessage(currentAlienName.ToUpper(), "is occupied at the moment. Come back later.");
            Debug.LogError("LLM Error: " + request.error);
            inputField.interactable = true; // unlock so player can retry
        }
    }

    IEnumerator TypeText(string fullText)
    {
        string temp = ""; // Clear the text component

        foreach (char c in fullText)
        {
            temp += c; // Append each letter one by one
            conversationText.text += c; // Update the text component with the new character
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)conversationText.transform);
            Canvas.ForceUpdateCanvases(); // Update the canvas to ensure the text is visible
            scrollRect.verticalNormalizedPosition = 0f; // Scroll to the bottom
            yield return new WaitForSeconds(0.02f); // Typing speed
        }

        // Tipying is done -> unlock unless chat is permanently locked
        alienTalking = false;

        // If we queued an end notice, print it now and lock forever
        if (!string.IsNullOrEmpty(_pendingSystemLine))
        {
            DisplayMessage("SYSTEM", _pendingSystemLine);
            _pendingSystemLine = null; // Clear the pending system line
            LockChatPermanently(); // Lock the chat permanently after the end notice
        }

        if (!_chatLocked)
        {
            inputField.interactable = true;
        }
    }

    private void LockChatPermanently()
    {
        _chatLocked = true;
        inputField.text = "";
        inputField.interactable = false;
    }

    private void UpdateAlienEmotion(string emotion, float score)
    {
        if (!alienEmotionImage.enabled)
        {
            alienEmotionImage.enabled = true; // Ensure the image is enabled
        }

        // Find the top emotion from the parsed distribution
        string topKey = "nautral"; // Default to neutral if no emotion is found
        float topValue = -1f; // Default value for neutral emotion

        foreach (var key in new[] { "joy", "sadness", "anger", "fear", "disgust" })
        {
            float val = alienData.emotionCounts.TryGetValue(key, out float value) ? value : 0f; // Get the emotion value or default to 0
            if (val > topValue)
            {
                topValue = val; // Update the top value
                topKey = key; // Update the top emotion key
            }
        }

        string spriteKey;
        if (topKey == "joy")
        {
            // Use the per-alien threshold when available
            float threshold = Mathf.Clamp01(_joyThresholdCache <= 0f ? 0.9f : _joyThresholdCache); // Default to 0.9 if no threshold is set
            spriteKey = (topValue >= threshold) ? "Joyful" : "Happy"; // Determine the sprite key based on the joy threshold
        }
        else if (topKey == "fear")
        {
            spriteKey = "Scared"; // Use Scared sprite for fear emotion
        }
        else if (topKey == "sadness")
        {
            spriteKey = "Sad"; // Use Sad sprite for sadness emotion
        }
        else if (topKey == "anger")
        {
            spriteKey = "Angry"; // Use Angry sprite for anger emotion
        }
        else if (topKey == "disgust")
        {
            spriteKey = "Disgusted"; // Use Disgusted sprite for disgust emotion
        }
        else
        {
            spriteKey = "Neutral"; // Default to Neutral if no other emotion matches
        }

        alienEmotionImage.sprite = LoadEmotion(currentAlienName, spriteKey); // Load the appropriate emotion sprite

        // Persist so it survives scene change
        alienData.lastEmotionKey = spriteKey; // Update the last emotion key for the alien
    }

    // Path would look like for example "AlienEmotions/ZAXIN/Joy
    private Sprite LoadEmotion(string alienName, string emotion)
    {
        // Capitalise the first letter
        string capitalizedEmotion = char.ToUpper(emotion[0]) + emotion.Substring(1).ToLower();
        return Resources.Load<Sprite>($"AlienEmotions/{alienName}/{capitalizedEmotion}");
    }

    IEnumerator ScrollToBottomNextFrame()
    {
        yield return null; // Wait for the end of the frame to ensure UI updates
        scrollRect.verticalNormalizedPosition = 0f; // Scroll to the bottom
    }

    private HistoryTurn[] BuildHistoryTurns(int maxTurns = 12)
    {
        if (alienData == null || alienData.chatHistory == null || alienData.chatHistory.Count == 0)
            return new HistoryTurn[0]; // Return empty if no history exists

        // Take the last N turns from the chat history
        var slice = alienData.chatHistory.Skip(Mathf.Max(0, alienData.chatHistory.Count - maxTurns)).ToList();
        var list = new System.Collections.Generic.List<HistoryTurn>(slice.Count);

        foreach (var line in slice)
        {
            // Expect "SENDER: message" format
            int idx = line.IndexOf(": ");
            if (idx <= 0) continue;

            string sender = line.Substring(0, idx).Trim(); // Extract the sender
            string content = line.Substring(idx + 2).Trim(); // Extract the message content

            string role = sender == "YOU" ? "user" // Determine the role based on sender
                : sender == "SYSTEM" ? "system"
                : "assistant"; // Default to assistant for alien messages

            // Skip system lines to avoid confusing LLM
            if (role == "system") continue;

            list.Add(new HistoryTurn
            {
                role = role, // Set the role for the turn
                content = content // Set the content for the turn
            });
        }

        return list.ToArray(); // Return the array of history turns
    }

    private void EnforceConclusionGateIfAny()
    {
        if (alienData != null && alienData.conversationClosed)
        {
            _chatLocked = true; // Lock the chat permanently
            inputField.interactable = false; // Disable input field interaction

            // Ensure the SYSTEM line is the last thing in history (avoid duplicates)
            string requiredLine = $"SYSTEM: {alienData.conclusionMessage}";
            if (alienData.chatHistory.Count == 0 || alienData.chatHistory[^1] != requiredLine)
            {
                DisplayMessage("SYSTEM", alienData.conclusionMessage); // Display the conclusion message
            }
        }
    }

    [System.Serializable]
    public class HistoryTurn
    {
        public string role; // The sender of the message (e.g., "YOU", "ALIEN_NAME")
        public string content; // The message content
    }

    [System.Serializable]
    private class ChatRequest
    {
        public string message; // The message from the player
        public string alienName; // The name of the alien character
        public HistoryTurn[] history; // Chat history for the alien
    }

    [System.Serializable]
    public class ChatResponse
    {
        public string reply; // The reply from the Groq API
        public Analysis analysis; // Analysis data from the Groq API
        public bool negotiationSuccess; // System response when alien is joyful and negotiation succeeded
        public bool negotiationFailure; // System response when alien is angry and negotiation failed
        public RL rl;
        public AlienProfile alienProfile; // Alien profile data from the Groq API

        [System.Serializable]
        public class Analysis
        {
            public string emotion; // Emotion detected by the Groq API
            public float emotionScore; // Emotion score from the Groq API
            public string distributionJson; // Emotion distribution as a JSON string
        }

        [System.Serializable]
        public class RL
        {
            public string stateKey;
            public string intent;
            public float reward;
        }

        [System.Serializable]
        public class AlienProfile
        {
            public string name; // Name of the alien
            public float joyThreshold; // Joy threshold for negotiation success
            public float angerTolerance; // Anger tolerance for negotiation failure
        }
    }

    [System.Serializable]
    public class DistributionWrapper
    {
        public string[] keys; // Array of emotion keys
        public float[] values; // Corresponding array of emotion values
    }
}
