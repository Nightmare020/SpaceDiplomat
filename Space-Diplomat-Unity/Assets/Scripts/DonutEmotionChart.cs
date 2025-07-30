using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class EmotionValue
{
    public string emotionName; // Name of the emotion
    [Range(0f, 100f)] public float percent; // Percentage of the emotion
    public Color color; // Color associated with the emotion
}

public class DonutEmotionChart : MonoBehaviour
{
    [Header("Emotion Data")]
    public Transform slicesRoot; // Root transform for the slices
    public Image slicePrefab; // Prefab for the slices
    public Image InnerCover; // Image for the inner cover donut hole
    public Image centerIcon; // Icon in the center of the donut chart

    [Header("Preview/Example Data")]
    public List<EmotionValue> emotions; // Example emotions for preview

    /// <summary>Call this whenever the values change</summary>
    public void Render()
    {
        // Clear existing slices
        //for (int i = slicesRoot.childCount - 1; i >= 0; i--)
        //{
        //    Destroy(slicesRoot.GetChild(i).gameObject);
        //}

        float total = Mathf.Max(0.0001f, emotions.Sum(e => Mathf.Max(0f, e.percent)));
        float start = 0f; // Accumulated fraction [0...1]

        foreach (var e in emotions)
        {
            float frac = Mathf.Max(0f, e.percent) / total; // Fraction of the total
            if (frac <= 0f) continue; // Skip if the fraction is zero

            // Create a new slice
            Image slice = Instantiate(slicePrefab, slicesRoot);
            slice.type = Image.Type.Filled; // Set the slice type to filled
            slice.fillMethod = Image.FillMethod.Radial360; // Use radial fill method
            slice.fillAmount = frac; // Set the fill amount based on the fraction
            slice.color = e.color; // Set the color of the slice

            // Rotate this slice to its start angle (0...360)
            float angle = start * 360f; // Convert fraction to angle
            slice.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angle); // Set rotation

            // Keep size equal to the chart rext
            var rt = slice.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            // Update the start angle for the next slice
            start += frac;
        }
    }

#if UNITY_EDITOR
    // For Unity Editor preview
    private void OnValidate()
    {
        if (slicesRoot && slicePrefab)
        {
            Render(); // Render the chart when values change in the editor
        }
    }
#endif
}
