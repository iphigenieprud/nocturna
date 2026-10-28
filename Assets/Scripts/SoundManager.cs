using System.Collections.Generic;
using UnityEngine;

// All the game's sounds in one place. Put this on an empty object called "SoundManager"
// and drag sound files into the slots. Any empty slot is simply silent, so you can add sounds bit by bit.
//
// Every slot has:
//   Clip     = the sound file
//   Start At = skip this many seconds at the beginning (cuts silence, or picks the part you like)
//   Length   = how many seconds to play (0 = until the end). Loops repeat only this part.
//
// What plays on its own (no other setup):
//  - ambience loop (from the start)
//  - the memory music: each memory you recover plays the NEXT part of your piano track,
//    then it fades and waits for the next memory. By the last memory you've heard the whole song.
//  - her HEARTBEAT, coming from her in 3D: faster and higher when she's angry
//  - footsteps while you walk (faster when sprinting)
//  - candle lit / candle blown out
//  - a soft hum from the tree that grows as it blooms
// The other scripts call the rest (her warning before a lunge, memories, getting hurt, endings).
public class SoundManager : MonoBehaviour
{
    [System.Serializable]
    public class Sound
    {
        public AudioClip clip;
        [Tooltip("Skip this many more seconds at the start (counted from where the sound really begins).")]
        [Min(0f)] public float startAt = 0f;
        [Tooltip("How many seconds to play. 0 = until the end of the file. Loops repeat just this part.")]
        [Min(0f)] public float length = 0f;

        public bool Has { get { return clip != null; } }
        public float From { get { return clip == null ? 0f : Mathf.Clamp(LeadSilence(clip) + startAt, 0f, Mathf.Max(0f, clip.length - 0.05f)); } }
        public float To
        {
            get
            {
                if (clip == null) return 0f;
                float end = length > 0f ? From + length : clip.length;
                return Mathf.Min(end, clip.length);
            }
        }
        public float Duration { get { return Mathf.Max(0.05f, To - From); } }
        public bool Trimmed { get { return clip != null && (From > 0.01f || To < clip.length - 0.01f); } }
    }

    public static SoundManager Instance;

    [Tooltip("Skip the silence at the start of every sound file automatically (Soundtrap exports often start with a silent gap). 'Start At' then counts from where the sound really begins.")]
    public bool skipSilenceAtStart = true;

    [Header("Volume")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float ambienceVolume = 0.5f;
    [Range(0f, 1f)] public float musicVolume = 0.3f;
    [Range(0f, 1f)] public float effectsVolume = 0.9f;

    [Header("Loops")]
    [Tooltip("Wind, drips, cave hum... loops all game from the start.")]
    public Sound ambience = new Sound();

    [Header("Memory Music")]
    [Tooltip("Your piano track. Each memory recovered plays the next part of it. 'Start At' / 'Length' choose which part of the file is used.")]
    public Sound music = new Sound();
    [Tooltip("OPTIONAL: the second in the file where each part ends (pick the end of a phrase so it doesn't cut mid-note). One number per memory, smallest first. Leave empty to split the track into equal parts.")]
    public float[] partEnds;
    [Tooltip("Special track for the LAST memory. It replaces the piano's last part and plays until an ending. Leave empty to keep the piano for all memories.")]
    public Sound lastMemoryMusic = new Sound();
    [Tooltip("Loop the special track until an ending (off = play it once).")]
    public bool loopLastMemoryMusic = true;

    [Header("Ambience Details")]
    [Tooltip("Short sounds played now and then at random spots around you in 3D (drips, pebbles, ice, far-off notes).")]
    public Sound[] details;
    [Tooltip("Seconds between details (min, max).")]
    public Vector2 detailInterval = new Vector2(4f, 12f);
    [Tooltip("A loop that comes from the water. Drag your water object into 'Water'.")]
    public Sound waterLoop = new Sound();
    public Transform water;
    [Tooltip("The world goes quieter when she's close, so her heartbeat takes over.")]
    [Range(0f, 1f)] public float quietNearHer = 0.7f;

    [Header("The Ghost")]
    [Tooltip("ONE heartbeat loop. Plays from her body in 3D; faster and higher when she's darker.")]
    public Sound heartbeat = new Sound();
    [Tooltip("Her warning right before she lunges: a whisper, a sharp inhale, a hiss.")]
    public Sound lungeWarning = new Sound();
    [Tooltip("When she fades out (a soft whoosh).")]
    public Sound vanish = new Sound();
    [Tooltip("When she forms again (quieter whoosh or breath).")]
    public Sound appear = new Sound();
    [Tooltip("Her long breath while blowing a candle out.")]
    public Sound blowing = new Sound();
    [Tooltip("When she dies.")]
    public Sound death = new Sound();

    [Header("Candles and Memories")]
    public Sound candleLight = new Sound();
    public Sound candleOut = new Sound();
    [Tooltip("A faint chime when a memory appears somewhere around you.")]
    public Sound memoryAppear = new Sound();
    [Tooltip("When you take a memory.")]
    public Sound memoryTaken = new Sound();
    [Tooltip("A soft hum/choir that loops at the tree, louder as it blooms.")]
    public Sound treeHum = new Sound();

    [Header("You")]
    [Tooltip("Several footstep sounds; one is picked at random each step.")]
    public Sound[] footsteps;
    [Tooltip("When she hits you.")]
    public Sound hurt = new Sound();
    [Tooltip("When lighting a candle hurts you (softer, sadder).")]
    public Sound candlePain = new Sound();

    [Header("Endings")]
    [Tooltip("For the sad endings.")]
    public Sound badEnding = new Sound();
    [Tooltip("For the best ending.")]
    public Sound bestEnding = new Sound();

    const float MusicFadeIn = 1.5f;     // seconds for each part to fade in
    const float MusicFadeOut = 2.5f;    // seconds for each part to fade out at its end

    class Loop { public AudioSource source; public Sound sound; }
    readonly List<Loop> loops = new List<Loop>();

    AudioSource ambienceSource, musicSource, heartSource, treeSource, lastSource;
    bool lastStarted;
    float lastFade;
    ShadowPerson ghost;
    GhostSmokeBody ghostSmoke;
    float stepTimer;
    float detailTimer = 3f;
    float herNearness;
    float loopsFade = 1f;
    bool ending;
    int partsPlayed;
    float partStart, partEnd;
    bool partPlaying;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        ambienceSource = Make2D(true);
        musicSource = Make2D(true);
        lastSource = Make2D(true);
    }

    void OnEnable()
    {
        Candle.AnyLit += OnCandleLit;
        Candle.AnyBlownOut += OnCandleOut;
    }

    void OnDisable()
    {
        Candle.AnyLit -= OnCandleLit;
        Candle.AnyBlownOut -= OnCandleOut;
    }

    void Start()
    {
        CheckSetup();

        StartLoop(ambienceSource, ambience);
        // (The music waits for your first candle)

        if (water != null && waterLoop.Has)
        {
            AudioSource w = Make3D(water, true, 4f, 40f);
            w.volume = masterVolume * ambienceVolume;
            StartLoop(w, waterLoop);
        }

        if (BloomTree.Instance != null && treeHum.Has)
        {
            treeSource = Make3D(BloomTree.Instance.transform, true, 3f, 30f);
            treeSource.transform.localPosition = Vector3.up * 2f;
            treeSource.volume = 0f;
            StartLoop(treeSource, treeHum);
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // Loops (they fade down during an ending)
        loopsFade = Mathf.MoveTowards(loopsFade, ending ? 0.25f : 1f, dt * 0.3f);

        // She's close: the world holds its breath
        if (ghost == null) ghost = FindObjectOfType<ShadowPerson>();
        float near = 0f;
        if (ghost != null && ghost.CanBeHit && Camera.main != null)
            near = Mathf.InverseLerp(15f, 4f, Vector3.Distance(ghost.transform.position, Camera.main.transform.position));
        herNearness = Mathf.MoveTowards(herNearness, near, dt);
        float hush = 1f - quietNearHer * herNearness;

        ambienceSource.volume = masterVolume * ambienceVolume * loopsFade * hush;
        UpdateMemoryMusic();

        UpdateLoops();
        UpdateDetails(dt, hush);
        UpdateHeartbeat(dt);
        UpdateFootsteps(dt);

        if (treeSource != null && BloomTree.Instance != null)
            treeSource.volume = Mathf.MoveTowards(treeSource.volume, masterVolume * effectsVolume * 0.6f * BloomTree.Instance.Bloom, dt * 0.5f);
    }

    // ---------- Loops that repeat only the part you chose ----------

    void StartLoop(AudioSource s, Sound sound)
    {
        if (s == null || !sound.Has) return;
        s.clip = sound.clip;
        s.loop = true;
        s.time = sound.From;
        s.Play();
        loops.Add(new Loop { source = s, sound = sound });
    }

    void UpdateLoops()
    {
        foreach (Loop l in loops)
        {
            if (l.source == null || !l.source.isPlaying || !l.sound.Trimmed) continue;
            float t = l.source.time;
            // Past the end of your part, or the file wrapped back to 0: jump back to "Start At"
            if (t >= l.sound.To - 0.01f || t < l.sound.From - 0.01f) l.source.time = l.sound.From;
        }
    }

    // ---------- Memory music: one part of the piano per memory ----------

    void PlayNextPart()
    {
        if (!music.Has) return;
        int parts = Mathf.Max(1, Memento.Total - (lastMemoryMusic.Has ? 1 : 0));
        if (partsPlayed >= parts) return;

        float end = PartEnd(partsPlayed, parts);
        partsPlayed++;

        if (partPlaying)
        {
            // Still playing the last part: just keep going into the next one, no jump
            partEnd = end;
            return;
        }

        partStart = partsPlayed == 1 ? music.From : PartEnd(partsPlayed - 2, parts);
        partEnd = end;
        musicSource.clip = music.clip;
        musicSource.loop = false;
        musicSource.time = partStart;
        musicSource.volume = 0f;
        musicSource.Play();
        partPlaying = true;
    }

    // Where part number 'i' ends (in seconds in the file)
    float PartEnd(int i, int parts)
    {
        if (i >= parts - 1) return music.To;   // the last part always plays to the end
        if (partEnds != null && i < partEnds.Length && partEnds[i] > music.From)
            return Mathf.Min(partEnds[i], music.To);
        return music.From + music.Duration * (i + 1) / parts;
    }

    // The last memory: the piano fades away and the special track begins
    void PlayLastMemoryMusic()
    {
        if (lastStarted) return;
        lastStarted = true;

        if (partPlaying) partEnd = Mathf.Min(partEnd, musicSource.time + 1.5f);

        if (loopLastMemoryMusic) StartLoop(lastSource, lastMemoryMusic);
        else
        {
            lastSource.clip = lastMemoryMusic.clip;
            lastSource.loop = false;
            lastSource.time = lastMemoryMusic.From;
            lastSource.Play();
        }
        lastSource.volume = 0f;
        lastFade = 0f;
        Debug.Log("SoundManager: last memory, special music.");
    }

    void UpdateLastMemoryMusic(float dt)
    {
        if (!lastStarted || !lastSource.isPlaying) return;

        // Played once and reached the end of the chosen part
        if (!loopLastMemoryMusic && lastSource.time >= lastMemoryMusic.To - 0.01f) { lastSource.Stop(); return; }

        // Fades in over 2 seconds; fades out over 3 seconds when an ending starts
        lastFade = Mathf.MoveTowards(lastFade, ending ? 0f : 1f, dt / (ending ? 3f : 2f));
        lastSource.volume = masterVolume * musicVolume * lastFade;
        if (ending && lastFade <= 0f) lastSource.Stop();
    }

    void UpdateMemoryMusic()
    {
        UpdateLastMemoryMusic(Time.deltaTime);
        if (!partPlaying) return;
        float t = musicSource.time;

        // Finished this part (or the file ended): stop and wait for the next memory
        if (!musicSource.isPlaying || t >= partEnd)
        {
            musicSource.Pause();
            partPlaying = false;
            return;
        }

        // Fade in at the start of the part, fade out at its end
        float fadeIn = Mathf.Clamp01((t - partStart) / MusicFadeIn);
        float fadeOut = Mathf.Clamp01((partEnd - t) / MusicFadeOut);
        musicSource.volume = masterVolume * musicVolume * loopsFade * Mathf.Min(fadeIn, fadeOut);
    }

    // ---------- Random details around you ----------

    void UpdateDetails(float dt, float hush)
    {
        if (details == null || details.Length == 0 || Camera.main == null) return;
        detailTimer -= dt;
        if (detailTimer > 0f) return;
        detailTimer = Random.Range(detailInterval.x, detailInterval.y);

        Sound c = details[Random.Range(0, details.Length)];
        if (c == null || !c.Has) return;
        Vector2 r = Random.insideUnitCircle.normalized * Random.Range(8f, 25f);
        Vector3 at = Camera.main.transform.position + new Vector3(r.x, Random.Range(-1f, 3f), r.y);
        PlayAt(c, at, ambienceVolume * Random.Range(0.4f, 0.9f) * hush / Mathf.Max(0.01f, effectsVolume), 45f);
    }

    // ---------- Her heartbeat ----------

    void UpdateHeartbeat(float dt)
    {
        if (!heartbeat.Has || ghost == null) return;

        if (heartSource == null)
        {
            ghostSmoke = ghost.GetComponent<GhostSmokeBody>();
            heartSource = Make3D(ghost.transform, true, 2f, 35f);
            heartSource.transform.localPosition = Vector3.up * 1.3f;
            heartSource.volume = 0f;
            StartLoop(heartSource, heartbeat);
        }

        // Heard only while she's here; faster and higher-pitched the darker she is
        float darkness = ghostSmoke != null ? ghostSmoke.Darkness : 0f;
        float want = ghost.CanBeHit ? masterVolume * effectsVolume : 0f;
        heartSource.volume = Mathf.MoveTowards(heartSource.volume, want, dt * 1.5f);
        heartSource.pitch = 0.85f + 0.5f * darkness;
    }

    // ---------- Footsteps ----------

    void UpdateFootsteps(float dt)
    {
        if (footsteps == null || footsteps.Length == 0) return;
        PlayerMovement pm = PlayerMovement.Instance;
        if (pm == null || !pm.enabled) return;

        bool moving = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);
        if (!moving || !pm.Grounded) { stepTimer = 0.1f; return; }

        stepTimer -= dt;
        if (stepTimer <= 0f)
        {
            stepTimer = pm.IsSprinting ? 0.32f : 0.5f;
            Sound c = footsteps[Random.Range(0, footsteps.Length)];
            if (c != null) Play2D(c, 0.35f, Random.Range(0.9f, 1.1f));
        }
    }

    // ---------- Called by other scripts ----------

    public void GhostWarning(Vector3 at) { PlayAt(lungeWarning, at, 1f); }
    public void GhostVanish(Vector3 at) { PlayAt(vanish, at, 0.6f); }
    public void GhostAppear(Vector3 at) { PlayAt(appear, at, 0.5f); }
    public void GhostBlow(Vector3 at) { PlayAt(blowing, at, 0.8f); }
    public void GhostDeath(Vector3 at) { PlayAt(death, at, 1f); }
    public void MemoryAppear(Vector3 at) { PlayAt(memoryAppear, at, 0.7f, 40f); }
    public void MemoryTaken() { Play2D(memoryTaken, 0.8f); }
    public void MemoryMusic()
    {
        if (lastMemoryMusic.Has && Memento.AllCollected) PlayLastMemoryMusic();
        else PlayNextPart();
    }
    public void PlayerHurt() { Play2D(hurt, 1f); }
    public void CandlePain() { Play2D(candlePain, 0.6f); }

    public void EndingSound(bool best)
    {
        ending = true;
        Play2D(best ? bestEnding : badEnding, 1f);
    }

    void OnCandleLit(Candle c)
    {
        if (Time.timeSinceLevelLoad < 0.5f) return;   // candles already lit at the start don't count
        PlayAt(candleLight, c.transform.position, 0.7f);
    }

    void OnCandleOut(Candle c) { PlayAt(candleOut, c.transform.position, 0.8f); }

    // ---------- Finds the usual reasons for "no sound" and tells you in the Console ----------

    void CheckSetup()
    {
        AudioListener[] listeners = FindObjectsOfType<AudioListener>();
        if (listeners.Length == 0)
        {
            Camera cam = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
            if (cam != null)
            {
                cam.gameObject.AddComponent<AudioListener>();
                Debug.LogWarning("SoundManager: there was no Audio Listener, so I added one to '" + cam.name + "'. Add it there yourself so it stays.", cam);
            }
            else Debug.LogError("SoundManager: no camera and no Audio Listener in the scene. Nothing can be heard.");
        }
        else if (listeners.Length > 1)
            Debug.LogWarning("SoundManager: " + listeners.Length + " Audio Listeners in the scene. Keep only the one on the player's camera.");

        if (AudioListener.volume <= 0.01f) { AudioListener.volume = 1f; Debug.LogWarning("SoundManager: the global volume was 0, set it back to 1."); }
        AudioListener.pause = false;
        if (masterVolume <= 0.01f) Debug.LogWarning("SoundManager: Master Volume is 0 in the Inspector.", this);

        int filled = 0;
        string empty = "";
        Check(ambience, "Ambience", ref filled, ref empty);
        Check(music, "Music", ref filled, ref empty);
        Check(waterLoop, "Water Loop", ref filled, ref empty);
        Check(heartbeat, "Heartbeat", ref filled, ref empty);
        Check(lungeWarning, "Lunge Warning", ref filled, ref empty);
        Check(candleLight, "Candle Light", ref filled, ref empty);
        Check(candleOut, "Candle Out", ref filled, ref empty);
        Check(memoryTaken, "Memory Taken", ref filled, ref empty);
        if (footsteps != null && footsteps.Length > 0 && footsteps[0] != null && footsteps[0].Has) filled++; else empty += "Footsteps, ";
        if (details != null && details.Length > 0 && details[0] != null && details[0].Has) filled++; else empty += "Details, ";

        if (filled == 0)
            Debug.LogWarning("SoundManager: ALL sound slots are empty. Click the SoundManager object and drag sound files into the 'Clip' of each slot.", this);
        else
            Debug.Log("SoundManager: " + filled + " sound slots filled. Empty (silent): " + (empty == "" ? "none" : empty.TrimEnd(',', ' ')), this);

        if (waterLoop.Has && water == null)
            Debug.LogWarning("SoundManager: Water Loop has a sound but 'Water' is empty. Drag your water object into 'Water'.", this);
    }

    void Check(Sound s, string label, ref int filled, ref string empty)
    {
        if (s != null && s.Has) filled++; else empty += label + ", ";
    }

    // ---------- Finding the silent gap at the start of a file ----------

    static readonly Dictionary<AudioClip, float> silenceCache = new Dictionary<AudioClip, float>();
    static readonly HashSet<AudioClip> warnedLoadType = new HashSet<AudioClip>();

    public static float LeadSilence(AudioClip c)
    {
        if (c == null || Instance == null || !Instance.skipSilenceAtStart) return 0f;
        float found;
        if (silenceCache.TryGetValue(c, out found)) return found;

        found = 0f;
        if (c.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            // Unity can only read the sound data when Load Type is "Decompress On Load"
            if (warnedLoadType.Add(c))
                Debug.Log("SoundManager: can't check '" + c.name + "' for silence at the start (its Load Type is " + c.loadType +
                          "). If it starts late, click the file and set Load Type to 'Decompress On Load', or use 'Start At'.", c);
            return 0f;
        }
        if (c.loadState != AudioDataLoadState.Loaded) c.LoadAudioData();
        if (c.loadState != AudioDataLoadState.Loaded) return 0f;   // try again next time

        int frames = Mathf.Min(c.samples, c.frequency * 10);       // look at the first 10 seconds
        float[] data = new float[frames * c.channels];
        if (c.GetData(data, 0))
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (Mathf.Abs(data[i]) > 0.01f)
                {
                    found = Mathf.Max(0f, (i / c.channels) / (float)c.frequency - 0.02f);
                    break;
                }
            }
        }
        if (found > 0.05f) Debug.Log("SoundManager: '" + c.name + "' starts with " + found.ToString("0.00") + " s of silence, skipping it.", c);
        silenceCache[c] = found;
        return found;
    }

    // ---------- Helpers ----------

    // A one-shot sound heard the same everywhere (not in 3D)
    void Play2D(Sound sound, float volume, float pitch = 1f)
    {
        if (sound == null || !sound.Has) return;
        GameObject go = new GameObject("Sound " + sound.clip.name);
        go.transform.SetParent(transform, false);
        AudioSource s = go.AddComponent<AudioSource>();
        s.spatialBlend = 0f;
        PlayPart(s, sound, masterVolume * effectsVolume * volume, pitch);
    }

    // A one-shot sound in 3D at a point in the world
    void PlayAt(Sound sound, Vector3 at, float volume, float maxDistance = 30f)
    {
        if (sound == null || !sound.Has) return;
        GameObject go = new GameObject("Sound " + sound.clip.name);
        go.transform.position = at;
        AudioSource s = go.AddComponent<AudioSource>();
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Logarithmic;
        s.minDistance = 2f;
        s.maxDistance = maxDistance;
        PlayPart(s, sound, masterVolume * effectsVolume * volume, Random.Range(0.95f, 1.05f));
    }

    // Plays only the part between "Start At" and "Length", then removes itself
    void PlayPart(AudioSource s, Sound sound, float volume, float pitch)
    {
        s.clip = sound.clip;
        s.playOnAwake = false;
        s.volume = volume;
        s.pitch = pitch;
        s.time = sound.From;
        s.Play();
        Destroy(s.gameObject, sound.Duration / Mathf.Max(0.1f, pitch) + 0.05f);
    }

    AudioSource Make2D(bool loop)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.loop = loop;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        return s;
    }

    AudioSource Make3D(Transform parent, bool loop, float minDist, float maxDist)
    {
        GameObject go = new GameObject("Sound Source");
        go.transform.SetParent(parent, false);
        AudioSource s = go.AddComponent<AudioSource>();
        s.loop = loop;
        s.playOnAwake = false;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Logarithmic;
        s.minDistance = minDist;
        s.maxDistance = maxDist;
        return s;
    }
}