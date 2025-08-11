using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.Linq;
using System.Collections;

public class DonutBuilder : MonoBehaviour
{
    private static string SERVER_BASE => ServerConfig.BaseUrl;
    private static string API_CHAT => SERVER_BASE + "/chat";
    private static string API_STATE => SERVER_BASE + "/alien_state";
    private static string API_HEALTH => SERVER_BASE + "/health";


    [System.Serializable]
    public class  Slice
    {
        public string emotionKey; // The key for the emotion
        public Image img; // The sprite for the emotion
    }

    [System.Serializable]
    private class AlienStateResponse
    {
        public string alien;
        public string distributionJson; // Emotion distribution as a JSON string
        public State state;
        public float joyThreshold; // Joy threshold for negotiation success
        public float angerTolerance; // Anger tolerance for negotiation failure 

        [System.Serializable]
        public class State
        {
            public float joy; // Joy emotion value
            public float anger; // Anger emotion value
        }
    }

    [System.Serializable]
    private class AlienNamePayload { public string alienName; }

    [System.Serializable]
    public class DistributionWrapper
    {
        public string[] keys; // Array of emotion keys
        public float[] values; // Corresponding array of emotion values
    }

    public List<Slice> slices = new List<Slice>(); // List of slices representing emotions
    public Image centerMask; // The center mask image

    // Control layout explicitly
    [Header("Layout")]
    [SerializeField] RectTransform chartRoot; // parent rect that contains all slices + center
    [SerializeField] float diameter = 600f; // pixels (befire CanvasScaler)
    [Range(0.1f, 0.9f)]
    [SerializeField] float innerHoleRatio = 0.58f; // inner hole = diameter * this

    // Fixed order around the circle (clockwise)
    static readonly string[] Order = { "disgust", "fear", "anger", "sadness", "joy"};

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Refresh();
    }

    private void OnEnable()
    {
        if (!GameBootstrap.BootDone)
        {
            GameBootstrap.Booted += OnBootedDonut;
            return;
        }

        SafeEnable();
    }

    private void OnDisable()
    {
        GameBootstrap.Booted -= OnBootedDonut;
        GameState.EmotionsChanged -= OnEmotionsChanged;
    }

    private void OnBootedDonut()
    {
        GameBootstrap.Booted -= OnBootedDonut;
        SafeEnable();
    }

    private void SafeEnable()
    {
        // Normalize rects in case layout changed
        NormalizeRects();

        // Try to get a fresh snapshot from server, the refresh UI
        StartCoroutine(FetchAndApplyState());

        GameState.EmotionsChanged += OnEmotionsChanged;
    }

    // Force identical anchors/size/position for all chart pieces
    void NormalizeRects()
    {
        if (chartRoot == null)
        {
            chartRoot = GetComponent<RectTransform>(); // fallback
        }

        Vector2 size = new Vector2(diameter, diameter);

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

    private IEnumerator FetchAndApplyState()
    {
        string alien = PlayerData.SelectedAlienName;
        if (string.IsNullOrEmpty(alien)) yield break;

        var payload = JsonUtility.ToJson(new AlienNamePayload { alienName = alien });

        using (var req = new UnityWebRequest(API_STATE, "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(payload);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 30;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<AlienStateResponse>(req.downloadHandler.text);

                // Update GameState so any other UI uses the same, fresh numbers
                var data = GameState.Instance.GetAlienData(alien);
                var dist = JsonUtility.FromJson<DistributionWrapper>(response.distributionJson);

                // Only let server override if this alien has in-session history, or it's Penbol
                bool allowServer =
                    string.Equals(alien, "PENBOL", System.StringComparison.OrdinalIgnoreCase) ||
                    (data.chatHistory != null && data.chatHistory.Count > 0);

                // detect flat ~0.2 each (server neutral prior)
                bool looksNeutral = true;
                if (dist.values != null && dist.values.Length == 5)
                {
                    for (int i = 0; i < dist.values.Length; i++)
                    {
                        if (Mathf.Abs(dist.values[i] - 0.2f) > 0.03f)
                        {
                            looksNeutral = false;
                            break;
                        }
                    }
                }

                if (allowServer && !looksNeutral)
                {
                    // Always take server truth, even if neutral
                    data.emotionCounts.Clear();
                    for (int i = 0;i < dist.keys.Length;i++)
                    {
                        var k = (dist.keys[i] ?? "").ToLowerInvariant();
                        data.emotionCounts[k] = Mathf.Max(0f, dist.values[i]);
                    }

                    // ENsure all 5 exist (fallback 0.2)
                    foreach (var k in new[] { "joy", "sadness", "anger", "fear", "disgust" })
                    {
                        if (!data.emotionCounts.ContainsKey(k))
                        {
                            data.emotionCounts[k] = 0.2f;
                        }
                    }

                    GameState.Instance.RaiseEmotionsChanged();
                }

                // Now repaint this donut
                Refresh();
            }
            else
            {
                // Fallback: just paint whatever GameState has
                Refresh();
                Debug.LogWarning("DonutBuilder: alien_state fetch failed: " + req.error);
            }
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

    private void OnEmotionsChanged(string alienName)
    {
        string current = PlayerData.SelectedAlienName;

        // if this donut is for Penbol, only fetch when other alien changed
        if (string.Equals(current, "PENBOL", System.StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(alienName, "PENBOL", System.StringComparison.OrdinalIgnoreCase))
            {

                StartCoroutine(FetchAndApplyState());
            }
            else
            {
                Refresh();
            }

            return;
        }

        // if this donut is for the same alien that just spoke...
        if (string.Equals(current, alienName, System.StringComparison.OrdinalIgnoreCase))
        {
            // Non-Penbol: we already have the latest counts locally, so just repaint
            Refresh();
        }
    }
}
