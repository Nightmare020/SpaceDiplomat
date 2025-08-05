using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class DonutBuilder : MonoBehaviour
{
    [System.Serializable]
    public class  Slice
    {
        public string emotionKey; // The key for the emotion
        public Image img; // The sprite for the emotion
    }

    public List<Slice> slices = new List<Slice>(); // List of slices representing emotions
    public Image centerMask; // The center mask image

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        string alien = PlayerData.SelectedAlienName; // Get the selected alien name from PlayerData
        if (string.IsNullOrEmpty(alien))
        {
            // No alien selected -> Hide the center mask completely
            gameObject.SetActive(false);
            return;
        }
        
        var counts = GameState.Instance.GetAlienData(alien).emotionCounts; // Get the emotion counts for the selected alien

        // Percetgaes
        float total = 0f;
        foreach (var count in counts.Values)
        {
            total += count;
        }

        if (total <= 0f)
        {
            total = 1f; // Avoid division by zero
        }

        float fillAmount = 0f; // Initialize fill amount
        foreach (var slice in slices)
        {
            float percentage = counts[slice.emotionKey] / total; // Calculate the percentage for each emotion
            slice.img.fillAmount = percentage; // Set the fill amount for the image
            slice.img.transform.localEulerAngles = new Vector3(0f, 0f, -fillAmount * 360f); // Set the rotation based on the fill amount
            fillAmount += percentage; // Update the fill amount for the next slice
        }

        // Keep the mask on top so it hides the middle of the donut
        if (centerMask != null)
        {
            centerMask.transform.SetAsLastSibling(); // Set the center mask as the last sibling to keep it on top
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // Load the spaceship scene
            SceneManager.LoadScene("SpaceshipMovementScene");
        }
    }
}
