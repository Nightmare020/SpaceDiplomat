using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ChatUI : MonoBehaviour
{
    public TMP_InputField inputField; // Reference to the input field for user text input
    public GameObject messagePrefab; // Prefab for the message UI element
    public Transform logContent; // Parent transform for the chat log content

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        inputField.onSubmit.AddListener(HandleInput); // Add listener for input field submission
    }

    void HandleInput(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText)) return; // Ignore empty input

        GameObject newMsg = Instantiate(messagePrefab, logContent); // Create a new message UI element
        TMP_Text msgText = newMsg.GetComponentInChildren<TMP_Text>(); // Get the text component of the message
        msgText.text = userText; // Set the text of the message to the user input

        inputField.text = ""; // Clear the input field after submission
        inputField.ActivateInputField(); // Reactivate the input field for further input
    }
}
