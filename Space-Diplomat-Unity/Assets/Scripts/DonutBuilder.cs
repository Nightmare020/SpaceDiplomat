using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Linq;

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

    // Control layout explicitly
    [Header("Layout")]
    [SerializeField] RectTransform chartRoot; // parent rect that contains all slices + center
    [SerializeField] float diamater = 600f; // pixels (befire CanvasScaler)
    [Range(0.1f, 0.9f)]
    [SerializeField] float innerHoleRatio = 0.58f; // inner hole = diameter * this

    // Fixed order around the circle (clockwise)
    static readonly string[] Order = { "disgust", "fear", "anger", "sadness", "joy"};

    // Awake is called when the script instance is being loaded
    private void Awake()
    {
        // Normalize rects so they always match
        NormalizeRects();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Refresh();
    }

    // Force identical anchors/size/position for all chart pieces
    void NormalizeRects()
    {
        if (chartRoot == null)
        {
            chartRoot = GetComponent<RectTransform>(); // fallback
        }

        Vector2 size = new Vector2(diamater, diamater);

        foreach (var sl in slices)
        {
            if (sl?.img == null) continue;
            RectTransform rt = sl.img.rectTransform;
            SnapToCenter(rt, size);
            sl.img.preserveAspect = true;
        }

        if (centerMask != null)
        {
            RectTransform rt = centerMask.rectTransform;
            SnapToCenter(rt, size * innerHoleRatio);
            centerMask.preserveAspect = true;
        }
    }

    public void Refresh()
    {
        string alien = PlayerData.SelectedAlienName; // Get the selected alien name from PlayerData
        if (string.IsNullOrEmpty(alien))
        {
            // No alien selected -> Hide the center mask completely
            gameObject.SetActive(false);
            return;
        }

        var counts = GameState.Instance.GetAlienData(alien).emotionCounts; // Get the emotion counts for the selected alien

        // Lowercase keys for safety
        Dictionary<string, float> keyValuePairs = new Dictionary<string, float>();
        foreach (var key in Order)
        {
            counts.TryGetValue(key, out float keyValue);
            keyValuePairs[key] = Mathf.Max(0f, keyValue);
        }

        // Percentages
        float total = keyValuePairs.Values.Sum();
        if (total <= 0.0001f)
        {
            foreach (var key in Order)
            {
                keyValuePairs[key] = 1f; // All 1 -> normalized to 0.2 each
            }

            total = keyValuePairs.Values.Sum();
        }

        // Normalize to [0..1] so they sum to 1
        Dictionary<string, float> weights = new Dictionary<string, float>();
        foreach (var key in Order)
        {
            weights[key] = keyValuePairs[key] / total;
        }

        // Find the UI images by key (ensure all 5 exist)
        Dictionary<string, Slice> byKey = new Dictionary<string, Slice>();
        foreach (var slice in slices)
        {
            if (slice == null || slice.img == null) continue;
            byKey[slice.emotionKey?.ToLower() ?? ""] = slice;
        }

        // Lay out wedges with direction rules
        float cursor = 0f; // accumulated angle in turns (0..1), clockwise baseline

        foreach (var key in Order)
        {
            if (!byKey.TryGetValue(key, out var sl) || sl.img == null)
                continue;

            float wedge = Mathf.Clamp01(weights[key]);
            var img = sl.img;

            // Make every slice radial - 360
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;

            if (key == "joy")
            {
                // Joy grows anti-clockwise
                img.fillClockwise = false;
                img.fillAmount = wedge;

                // Rotate so that the end of joy aligns with the running cursor
                // (keep seams tight)
                float startAngle = -(cursor + wedge) * 360f;
                img.transform.localEulerAngles = new Vector3(0f, 0f, startAngle);
            }
            else
            {
                // Others grow clockwise
                img.fillClockwise = true;
                img.fillAmount = wedge;

                // Start at current cursor angle
                float startAngle = -cursor * 360f;
                img.transform.localEulerAngles = new Vector3(0f, 0f, startAngle);
            }

            // Advance along the ring for the next slice
            cursor += wedge;
        }

        // Keep the mask on top so it hides the middle of the donut
        if (centerMask != null)
        {
            centerMask.transform.SetAsLastSibling(); // Set the center mask as the last sibling to keep it on top
        }
    }

    static void SnapToCenter(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }
}
