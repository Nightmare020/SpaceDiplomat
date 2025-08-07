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
        foreach (string line in alienData.chatHistory)
        {
            conversationText.text += line + "\n"; // Display each line from the chat history
        }
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
            StartCoroutine(SendMessageToLlama(message));
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

    IEnumerator SendMessageToLlama(string playerInput)
    {
        // Build a typed payload so JsonUtility makes correct JSON
        var payload = new ChatRequest { message = playerInput, alienName = currentAlienName };
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

            // Check end condition from the server
            if (response.negotiationSuccess)
            {
                DisplayMessage("SYSTEM", "Diplomatic solution reached. Talks Concluded.");
                LockChatPermanently();
                yield break;
            }

            if (response.negotiationFailure)
            {
                DisplayMessage("SYSTEM", "Negotiation failed. The alien refuses to continue.");
                LockChatPermanently();
                yield break;
            }

            alienTalking = true;
            DisplayMessage(currentAlienName, response.reply);

            // Update counts using full vector
            if (!string.IsNullOrEmpty(response.analysis.distributionJson))
            {
                var wrapper = JsonUtility.FromJson<DistributionWrapper>(response.analysis.distributionJson);

                // Reset the five cannonical buckets
                foreach (var key in new[] { "joy", "sadness", "anger", "disgust", "fear" })
                    alienData.emotionCounts[key] = 0f;

                for (int i = 0; i < wrapper.keys.Length; i++)
                {
                    string key = (wrapper.keys[i] ?? "").ToLower(); // Normalize the key to lowercase
                    float value = Mathf.Max(0f, wrapper.values[i]); // Ensure the value is non-negative

                    // Keep only the five canonical emotions
                    if (key is "joy" or "sadness" or "anger" or "disgust" or "fear")
                    {
                        alienData.emotionCounts[key] = value; // Increment the count for the emotion
                    }
                }
            }

            if (response.analysis != null)
            {
                UpdateAlienEmotion(response.analysis.emotion, response.analysis.emotionScore);
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

        string statKey = (emotion ?? "").ToLower(); // Normalize the emotion key to lowercase
        string spriteKey;

        switch (statKey)
        {
            case "joy":
                spriteKey = (score >= 0.8f) ? "Joyful" : "Happy";
                break;

            case "fear":
                spriteKey = "Scared";
                break;

            case "sadness":
                spriteKey = "Sad";
                break;

            case "anger":
                spriteKey = "Angry";
                break;

            case "disgust":
                spriteKey = "Disgusted";
                break;

            default:
                spriteKey = "Neutral"; // Default emotion
                break;
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

    [System.Serializable]
    private class ChatRequest
    {
        public string message; // The message from the player
        public string alienName; // The name of the alien character
    }

    [System.Serializable]
    public class ChatResponse
    {
        public string reply; // The reply from the llama.cpp API
        public Analysis analysis; // Analysis data from the llama.cpp API
        public bool negotiationSuccess; // System response when alien is joyful and negotiation succeeded
        public bool negotiationFailure; // System response when alien is angry and negotiation failed
        public RL reinfrocement;

        [System.Serializable]
        public class Analysis
        {
            public string emotion; // Emotion detected by the llama.cpp API
            public float emotionScore; // Emotion score from the llama.cpp API
            public string distributionJson; // Emotion distribution as a JSON string
        }

        [System.Serializable]
        public class RL
        {
            public string stateKey;
            public string intent;
            public float reward;
        }
    }

    [System.Serializable]
    public class DistributionWrapper
    {
        public string[] keys; // Array of emotion keys
        public float[] values; // Corresponding array of emotion values
    }
}
