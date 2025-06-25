using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;

public class ChatManager : MonoBehaviour
{
    public TMP_InputField inputField; // Input field for user messages
    public GameObject messagePrefab; // Prefab for displaying messages
    public Transform contentArea; // Parent object for messages in the UI
    public ScrollRect scrollRect; // ScrollRect to manage scrolling

    private const string API_URL = "http://127.0.0.1:5000/chat";

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            string message = inputField.text.Trim();

            if (!string.IsNullOrEmpty(message))
            {
                DisplayMessage("YOU", message);
                StartCoroutine(SendMessageToGroq(message));
                inputField.text = ""; // Clear input field after sending
                inputField.ActivateInputField(); // Reactivate input field
            }
        }
    }

    void DisplayMessage(string sender, string message)
    {
        GameObject msg = Instantiate(messagePrefab, contentArea);
        TMP_Text textComp = msg.GetComponentInChildren<TMP_Text>();
        textComp.text = $"> {message}";

        Canvas.ForceUpdateCanvases(); // Update the canvas to ensure the new message is visible
        scrollRect.verticalNormalizedPosition = 0f; // Scroll to the bottom
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
            string jsonResponse = request.downloadHandler.text;
            string reply = JsonUtility.FromJson<ChatResponse>(jsonResponse).reply;
            DisplayMessage("XARNON", reply);
        }
        else
        {
            DisplayMessage("SYSTEM", "Error contacting Groq API");
            Debug.LogError("Error: " + request.error);
        }
    }

    [System.Serializable]
    public class ChatResponse
    {
        public string reply; // The reply from the Groq API
    }
}
