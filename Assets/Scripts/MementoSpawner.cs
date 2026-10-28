using System.Collections.Generic;
using UnityEngine;

// Each time you light a candle, a new memory appears on the ground somewhere around you,
// until all of them have appeared. Their order is shuffled every game.
// Put this on any object in the scene (for example the EndingManager).
public class MementoSpawner : MonoBehaviour
{
    [System.Serializable]
    public class MementoType
    {
        [TextArea(2, 3)] public string memoryLine;
        public string prompt = "[E]   remember";
    }

    [Tooltip("Soft round particle material. GhostFogMat works.")]
    public Material glowMaterial;
    [Tooltip("The color of every memory's light.")]
    public Color glowColor = new Color(1f, 0.85f, 0.6f);
    [Tooltip("Closest a memory can appear to you (meters).")]
    public float minDistance = 6f;
    [Tooltip("Farthest a memory can appear from you (meters).")]
    public float maxDistance = 15f;
    [Tooltip("How high above the ground the light floats.")]
    public float heightAboveGround = 0.4f;

    [Tooltip("Shown once, when the first memory appears.")]
    [TextArea(2, 3)] public string firstAppearLine = "Something of yours is out there.\nIt's waiting for you.";

    [Tooltip("One entry per memory. They appear in a random order.")]
    public List<MementoType> types = new List<MementoType>
    {
        new MementoType { memoryLine = "A song you used to hum.\nYou still know the words.", prompt = "[E]   listen" },
        new MementoType { memoryLine = "Warm hands around a cup.\nSomeone waiting up for you.", prompt = "[E]   remember" },
        new MementoType { memoryLine = "Your name, said gently,\nfrom another room.", prompt = "[E]   answer" },
        new MementoType { memoryLine = "A door left open.\nA light left on.", prompt = "[E]   go in" },
        new MementoType { memoryLine = "Laughing at something small.\nYou can't remember what.", prompt = "[E]   hold on" },
        new MementoType { memoryLine = "A coat on a hook.\nNobody has moved it.", prompt = "[E]   don't let go" },
    };

    readonly List<MementoType> order = new List<MementoType>();
    int spawned;
    int waiting;          // candles lit while a memory was still out there: they each bring one later
    Memento current;
    float nextAllowed;

    void OnEnable() { Candle.AnyLit += OnCandleLit; }
    void OnDisable() { Candle.AnyLit -= OnCandleLit; }

    void Start()
    {
        if (glowMaterial == null) Debug.LogWarning("MementoSpawner: drag GhostFogMat into 'Glow Material'.");

        // Mementos placed by hand in the scene would show up before any candle: remove them
        foreach (Memento m in Memento.All.ToArray())
        {
            if (m == null) continue;
            Debug.Log("MementoSpawner: removed the memento you placed by hand ('" + m.name + "'). Memories now only appear when candles are lit.", m);
            Destroy(m.gameObject);
        }

        // Shuffle the order for this game
        order.AddRange(types);
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            MementoType t = order[i]; order[i] = order[j]; order[j] = t;
        }

        // The counter shows the full total from the start
        Memento.ExpectedTotal = order.Count;
    }

    void OnCandleLit(Candle c)
    {
        if (Time.timeSinceLevelLoad < 0.5f) return;   // candles lit at the start don't count
        if (spawned + waiting >= order.Count) return;

        Transform player = FindPlayer();
        if (player == null) return;

        // Only candles YOU light (you're standing next to it)
        Vector3 d = c.transform.position - player.position;
        d.y = 0f;
        if (d.magnitude > 5f) return;

        // One memory at a time: if one is still out there, this candle's memory waits its turn
        if (current != null && !current.Collected) { waiting++; return; }
        Spawn(player);
    }

    float allLitSince = -1f;

    void Update()
    {
        // Every candle is already burning, so there's nothing left to light:
        // the next memory appears on its own after a moment (so you're never stuck)
        bool more = spawned + waiting < order.Count;
        bool free = current == null || current.Collected;
        if (more && free && waiting == 0 && AllCandlesLit())
        {
            if (allLitSince < 0f) allLitSince = Time.time;
            if (Time.time - allLitSince > 8f) { allLitSince = -1f; waiting++; }
        }
        else allLitSince = -1f;

        // The previous memory was taken: a waiting one appears a little later
        if (waiting > 0 && (current == null || current.Collected))
        {
            if (nextAllowed <= 0f) nextAllowed = Time.time + 6f;
            if (Time.time >= nextAllowed)
            {
                Transform player = FindPlayer();
                if (player != null && Spawn(player)) { waiting--; nextAllowed = 0f; }
            }
        }
    }

    // Returns false if no spot was found (it will try again, the memory isn't lost)
    bool Spawn(Transform player)
    {
        Vector3 spot;
        if (!FindSpot(player, out spot))
        {
            // No ground found around you: put it right next to you instead
            spot = player.position + player.forward * 2f + Vector3.up * heightAboveGround;
        }

        MementoType t = order[spawned];
        spawned++;

        GameObject go = new GameObject("Memento " + spawned);
        go.transform.position = spot;
        Memento m = go.AddComponent<Memento>();
        m.memoryLine = t.memoryLine;
        m.prompt = t.prompt;
        m.glowColor = glowColor;
        m.glowMaterial = glowMaterial;
        current = m;
        if (SoundManager.Instance != null) SoundManager.Instance.MemoryAppear(spot);

        Debug.Log("MementoSpawner: a memory appeared (" + spawned + " / " + order.Count + ").");

        if (spawned == 1 && UIManager.Instance != null) UIManager.Instance.ShowMemory(firstAppearLine);
        return true;
    }

    // A random spot on the ground around the player
    bool FindSpot(Transform player, out Vector3 spot)
    {
        for (int tries = 0; tries < 15; tries++)
        {
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(minDistance, maxDistance);
            Vector3 p = player.position + new Vector3(r.x, 0f, r.y);

            float best = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 50f, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(player.root)) continue;
                if (hit.point.y > best) best = hit.point.y;
            }
            if (float.IsNegativeInfinity(best)) continue;

            spot = new Vector3(p.x, best + heightAboveGround, p.z);
            return true;
        }
        spot = Vector3.zero;
        return false;
    }

    bool AllCandlesLit()
    {
        if (Candle.All.Count == 0) return false;
        foreach (Candle c in Candle.All) if (c != null && !c.IsLit) return false;
        return true;
    }

    Transform FindPlayer()
    {
        if (PlayerMovement.Instance != null) return PlayerMovement.Instance.transform;
        GameObject p = GameObject.Find("PLAYER");
        if (p == null) p = GameObject.FindWithTag("Player");
        return p != null ? p.transform : null;
    }
}