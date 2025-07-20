using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class InteractObject : MonoBehaviour
{
    public TextMeshProUGUI messageText; // Reference to the UI element that displays interaction messages
    public string sceneToLoad; // Name of the scene to load when the player interacts with the object

    private bool playerInRange = false; // Flag to check if the player is in range to interact

    // Update is called once per frame
    void Update()
    {
        // Check if the player is in range and presses the interaction key (E)
        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            PlayerData.savedPosition = GameObject.FindWithTag("Player").transform.position; // Save the player's position
            PlayerData.savedRotation = GameObject.FindWithTag("Player").transform.rotation; // Save the player's rotation
            PlayerData.hasSavedPosition = true;

            // Load the specified scene
            SceneManager.LoadScene(sceneToLoad);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Check if the player enters the trigger area
        if (other.CompareTag("Player"))
        {
            // Set the playerInRange flag to true
            playerInRange = true;

            // Show the interaction message
            messageText.gameObject.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // Check if the player exits the trigger area
        if (other.CompareTag("Player"))
        {
            // Set the playerInRange flag to false
            playerInRange = false;

            // Hide the interaction message
            messageText.gameObject.SetActive(false);
        }
    }
}
