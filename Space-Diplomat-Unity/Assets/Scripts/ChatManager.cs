using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using UnityEngine.SceneManagement;
using Unity.VisualScripting;

public class ChatManager : MonoBehaviour
{
    public TMP_InputField inputField; // Input field for user messages
    public TMP_Text conversationText; // Text for message displaying
    public Transform contentArea; // Parent object for messages in the UI
    public ScrollRect scrollRect; // ScrollRect to manage scrolling

    public Image fillBar; // Fill bar for visual max char feedback
    public int maxChars = 300; // Maximum number of words allowed in a message
    public Image alienEmotionImage; // Image to display alien emotion

    private const string API_URL = "http://127.0.0.1:5000/chat";

    // Start is called before the first frame update
    void Start()
    {
        StartCoroutine(FocusInput());
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            SubmitMessage();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // Load the spaceship scene
            SceneManager.LoadScene("SpaceshipMovementScene");
        }

        UpdateCharacterCountBar();
    }

    IEnumerator FocusInput()
    {
        yield return null; // Wait for the end of the frame to ensure UI is ready

        inputField.Select(); // Select the input field
        inputField.ActivateInputField(); // Activate the input field for user input
    }

    void SubmitMessage()
    {
        string message = inputField.text.Trim();

        if (!string.IsNullOrEmpty(message))
        {
            DisplayMessage("YOU", message);
            StartCoroutine(SendMessageToGroq(message));
            inputField.text = ""; // Clear the input field
            inputField.ActivateInputField(); // Reactivate the input field for new input
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
        if (sender == "XARNON")
            StartCoroutine(TypeText($"\n> {message}\n\n"));
        else
        {
            string formattedMessage = $"> {message}\n";
            conversationText.text += formattedMessage;

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)conversationText.transform);
            Canvas.ForceUpdateCanvases(); // Update the canvas to ensure the new message is visible
            StartCoroutine(ScrollToBottomNextFrame()); // Scroll to the bottom
        }
    }

    IEnumerator SendMessageToGroq(string playerInput)
    {
        string json = "{\"message\":\"" + playerInput.Replace("\"", "\\\"") + "\"}";
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);

        UnityWebRequest request = new UnityWebRequest(API_URL, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            ChatResponse response = JsonUtility.FromJson<ChatResponse>(request.downloadHandler.text);
            DisplayMessage("XARNON", response.reply);

            if (response.analysis != null)
            {
                UpdateAlienEmotion(response.analysis.emotion);
            }
        }
        else
        {
            DisplayMessage("SYSTEM", "Error contacting Groq API");
            Debug.LogError("Error: " + request.error);
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
    }

    private void UpdateAlienEmotion(string emotion)
    {
        string emotionKey = emotion.ToLower();

        switch (emotionKey)
        {
            case "joy":
                alienEmotionImage.sprite = Resources.Load<Sprite>("AlienEmotions/Introvert_Joy");
                break;

            case "happy":
                alienEmotionImage.sprite = Resources.Load<Sprite>("AlienEmotions/Introvert_Happy");
                break;
            
            case "neutral":
                alienEmotionImage.sprite = Resources.Load<Sprite>("AlienEmotions/Introvert_Neutral");
                break;

            case "sadness":
                alienEmotionImage.sprite = Resources.Load<Sprite>("AlienEmotions/Introvert_Sad");
                break;

            case "anger":
                alienEmotionImage.sprite = Resources.Load<Sprite>("AlienEmotions/Introvert_Rage");
                break;
            
            case "surprise":
                alienEmotionImage.sprite = Resources.Load<Sprite>("AlienEmotions/Introvert_Surprise");
                break;

            default:
                alienEmotionImage.sprite = Resources.Load<Sprite>("AlienEmotions/Introvert_Neutral"); // Default emotion
                break;
        }
    }

    IEnumerator ScrollToBottomNextFrame()
    {
        yield return null; // Wait for the end of the frame to ensure UI updates
        scrollRect.verticalNormalizedPosition = 0f; // Scroll to the bottom
    }

    [System.Serializable]
    public class ChatResponse
    {
        public string reply; // The reply from the Groq API
        public Analysis analysis; // Analysis data from the Groq API

        [System.Serializable]
        public class Analysis
        {
            public string emotion; // Sentiment analysis result
            public string emotion_score; // Intent analysis result
        }
    }
}
