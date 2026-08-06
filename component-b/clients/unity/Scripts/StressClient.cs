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

    [System.Serializable]
    public class StressPrediction
    {
        public string mode;
        public int level;
        public string label;
        public float confidence;
        public string baseline_maturity;
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
        Debug.Log($"stress={p.label} mode={p.mode} conf={p.confidence}");
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
