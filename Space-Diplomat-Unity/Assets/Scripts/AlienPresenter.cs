using UnityEngine;
using UnityEngine.UI;

public class AlienPresenter : MonoBehaviour
{
    public Image alienImage; // Image component to display the alien sprite

    [SerializeField] private string overrideAlienName;

    private string MyAlien =>
        string.IsNullOrEmpty(overrideAlienName) ? PlayerData.SelectedAlienName : overrideAlienName;

    private void OnEnable()
    {
        GameState.EmotionsChanged += OnEmotionsChanged;
        RefreshSprite();
    }

    private void OnDisable()
    {
        GameState.EmotionsChanged -= OnEmotionsChanged;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RefreshSprite();
    }

    private void RefreshSprite()
    {
        string alienName = MyAlien;
        if (string.IsNullOrEmpty(alienName) || alienImage == null)
        {
            if (alienImage)
                alienImage.enabled = false;
            return;
        }

        var data = GameState.Instance.GetAlienData(alienName);
        alienImage.sprite = Resources.Load<Sprite>($"AlienEmotions/{alienName}/{data.lastEmotionKey}");
        alienImage.enabled = alienImage.sprite != null;
    }

    private void OnEmotionsChanged(string changedAlien)
    {
        // Update only when alien changed or on a broadcast (null)
        if (changedAlien == null || 
            string.Equals(changedAlien, MyAlien, System.StringComparison.OrdinalIgnoreCase))
        {
            RefreshSprite();
        }
    }
}
