using UnityEngine;
using UnityEngine.UI;

public class AlienPresenter : MonoBehaviour
{
    public Image alienImage; // Image component to display the alien sprite

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        string alienName = PlayerData.SelectedAlienName; // Get the selected alien name from PlayerData

        if (string.IsNullOrEmpty(alienName) || alienImage == null)
        {
            // No alien selected -> Hide the image completely
            alienImage.enabled = false;
            return;
        }

        var alienData = GameState.Instance.GetAlienData(alienName); // Get the alien data for the selected alien
        alienImage.sprite = Resources.Load<Sprite>($"AlienEmotions/{alienName}/{alienData.lastEmotionKey}");
        alienImage.enabled = alienImage.sprite; // Show the image if an alien is selected
    }
}
