// Quest 2 client. Requires NativeWebSocket:
//   https://github.com/endel/NativeWebSocket
using UnityEngine;
using NativeWebSocket;

public class StressClient : MonoBehaviour
{
    [Tooltip("Laptop IP on the local network")]
    public string serverHost = "192.168.1.100";
    public int serverPort = 8000;

    WebSocket ws;

    // Mirrors server/schemas/messages.py. The stress decision is nested;
    // JsonUtility handles nested [System.Serializable] classes, but NOT
    // dictionaries — `probabilities` is therefore not modelled here. Use a
    // real JSON library if Component C needs the distribution.
    [System.Serializable]
    public class StressBlock
    {
        public string mode;          // "point" or "band"
        public int level;            // point mode
        public int level_low;        // band mode
        public int level_high;       // band mode
        public string label;
        public float confidence;     // margin between top two classes
        public bool adjacent;
        public float continuous_score;
    }

    [System.Serializable]
    public class StressPrediction
    {
        public double timestamp;     // POSIX seconds; equals windowEnd
        public float heartRate;      // bpm
        public float rmssd;          // ms
        public float sdnn;           // ms
        public StressBlock stress;
        public float signalQuality;  // heartbeat-data quality, NOT BLE signal
        public double windowStart;
        public double windowEnd;
    }

    async void Start()
    {
        ws = new WebSocket($"ws://{serverHost}:{serverPort}/stream");
        ws.OnMessage += (bytes) =>
        {
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var p = JsonUtility.FromJson<StressPrediction>(json);
            OnStressUpdate(p);
        };
        await ws.Connect();
    }

    void OnStressUpdate(StressPrediction p)
    {
        // A "band" means the model is uncertain between adjacent
        // levels — prefer a gentler, less specific response here.
        // Do NOT collapse a band to a single level; that is exactly the
        // false precision the confidence gate exists to avoid.
        Debug.Log($"stress={p.stress.label} mode={p.stress.mode} " +
                  $"conf={p.stress.confidence} hr={p.heartRate} " +
                  $"quality={p.signalQuality}");
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    async void OnApplicationQuit()
    {
        if (ws != null) await ws.Close();
    }
}
