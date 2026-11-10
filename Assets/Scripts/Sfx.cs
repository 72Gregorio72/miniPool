using UnityEngine;

// Sound effects and background music from Resources/Audio (generated with ElevenLabs):
// one folder per kind (Ball, Cushion, Cue, Pocket) with any number of variants, picked at random, plus an optional <Kind>Soft folder
// for gentle hits (a soft hit sounds different, not just quieter: played quiet, a hard hit sounds far away). Ball/Cushion/Cue are recorded
// hits (Billiards Sound Pack), named low -> high pitched and picked by strength instead. Music = looping lounge track.
// Hits are collected per frame and the loudest of each kind plays once, so a break doesn't stack up dozens of voices.
// shortcut: multiplayer clients don't run the physics, so they only hear their own cue; send hits in the state if it matters
public class Sfx : MonoBehaviour
{
    // Boost..Gate = modules (Land = a flying ball touching down), Click..Crown = UI; clips made by Tools/make_sfx.mjs
    public enum Kind { Ball, Cushion, Cue, Pocket, Boost, Fan, Land, Portal, Bumper, Manhole, Spikes, Magnet, Button, Gate, Click, Win, Fail, Crown }

    const float SfxVolume = 0.7f, MusicVolume = 0.22f; // music under the ball clicks (0.3 covered them)
    const float Soft = 0.25f;     // below this a hit uses the soft variants
    // seconds before the same kind plays again (unless louder): a cue ball with follow keeps nudging the ball it stopped behind,
    // and those micro-contacts played every few frames buzz like a broken TV
    const float Repeat = 0.15f;
    // seconds kept of each kind (by Kind): the generated clips trail off in hiss that sounds like a broken TV
    static readonly float[] MaxLength = { 0.3f, 0.6f, 0.65f, 0.5f, 0.45f, 0.4f, 0.25f, 0.5f, 0.35f, 0.4f, 0.4f, 0.35f, 0.15f, 0.4f, 0.1f, 1.4f, 0.7f, 2.3f };
    // by Kind (production/audio/sfx-e-musica/sfx-spec.md): loudness in the mix, and random pitch ± % (0 for the tonal ones, in key with the music)
    static readonly float[] Gain = { 0.85f, 0.75f, 0.8f, 0.8f, 0.6f, 0.6f, 0.65f, 0.6f, 0.7f, 0.65f, 0.7f, 0.6f, 0.6f, 0.65f, 0.5f, 0.7f, 0.45f, 0.8f };
    static readonly float[] Pitch = { 4, 4, 3, 3, 5, 5, 4, 0, 5, 4, 3, 4, 3, 4, 2, 0, 0, 0 };
    static Sfx inst;
    AudioSource[] sources; // one per kind: pitch is per source
    AudioSource music;
    AudioClip[][] clips, softClips;
    static readonly int KindCount = System.Enum.GetValues(typeof(Kind)).Length;
    readonly float[] pending = new float[KindCount], lastAt = new float[KindCount], lastVolume = new float[KindCount];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst == null) inst = new GameObject("Sfx").AddComponent<Sfx>();
    }

    // music level 0..1 from the settings screen (the master volume is AudioListener.volume)
    public static float Music
    {
        get => PlayerPrefs.GetFloat("MiniPool.music", 1);
        set { PlayerPrefs.SetFloat("MiniPool.music", value); if (inst != null) inst.music.volume = MusicVolume * value; }
    }

    // strength 0..1 (clamped): how hard the hit is; no-op outside Play mode (EditMode tests run the physics too)
    public static void Play(Kind kind, float strength)
    {
        if (!Application.isPlaying || inst == null || strength < 0.05f) return;
        inst.pending[(int)kind] = Mathf.Max(inst.pending[(int)kind], Mathf.Min(strength, 1));
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

        var kinds = (Kind[])System.Enum.GetValues(typeof(Kind));
        clips = new AudioClip[kinds.Length][];
        softClips = new AudioClip[kinds.Length][];
        sources = new AudioSource[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            clips[i] = Load("Audio/" + kinds[i], MaxLength[i], i <= (int)Kind.Cue);
            softClips[i] = Load("Audio/" + kinds[i] + "Soft", MaxLength[i]);
            sources[i] = gameObject.AddComponent<AudioSource>();
        }

        music = gameObject.AddComponent<AudioSource>();
        music.clip = Resources.Load<AudioClip>("Audio/Music");
        music.loop = true;
        music.volume = MusicVolume * Music;
        if (music.clip != null) music.Play();
    }

    void LateUpdate()
    {
        for (int i = 0; i < pending.Length; i++)
        {
            float s = pending[i];
            pending[i] = 0;
            if (s <= 0 || Time.unscaledTime - lastAt[i] < Repeat && s <= lastVolume[i]) continue;
            bool soft = s < Soft && softClips[i].Length > 0;
            var set = soft ? softClips[i] : clips[i];
            if (set.Length == 0) continue;
            // each set covers its own range of strength: soft variants quiet, hard ones loud
            float lo = !soft && softClips[i].Length > 0 ? Soft : 0, hi = soft ? Soft : 1, t = Mathf.InverseLerp(lo, hi, s);
            float volume = soft ? Mathf.Lerp(0.2f, 0.6f, t) : Mathf.Lerp(lo > 0 ? 0.6f : 0.4f, 1, t);
            // Ball/Cushion/Cue variants are named low -> high pitched (recorded hits, cut by brightness): a harder hit picks a higher one,
            // ± half a step so equal hits still vary; the others are random
            var clip = i <= (int)Kind.Cue
                ? set[Mathf.Clamp(Mathf.RoundToInt(s * (set.Length - 1) + Random.Range(-0.5f, 0.5f)), 0, set.Length - 1)]
                : set[Random.Range(0, set.Length)];
            sources[i].pitch = 1 + Random.Range(-Pitch[i], Pitch[i]) / 100;
            sources[i].PlayOneShot(clip, volume * Gain[i] * SfxVolume);
            lastAt[i] = Time.unscaledTime; // the pause menu stops Time.time
            lastVolume[i] = s;
        }
    }

    static AudioClip[] Load(string path, float maxSeconds, bool keepTail = false)
    {
        var all = Resources.LoadAll<AudioClip>(path);
        System.Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name)); // LoadAll order isn't guaranteed; graded kinds need it
        return System.Array.FindAll(System.Array.ConvertAll(all, c => Clean(c, maxSeconds, keepTail)), c => c != null);
    }

    // Trims by loudness over 5 ms windows: starts at the attack, ends where the sound drops below -24 dB of its loudest part
    // (the low hiss after it is cut), fades the end and brings every variant to the same peak, so they all match.
    // A clip still loud after `maxSeconds` has noise in it (sounds like a broken TV): skipped (null) with a warning.
    static AudioClip Clean(AudioClip c, float maxSeconds, bool keepTail)
    {
        int ch = c.channels, n = c.samples, win = Mathf.Max(1, c.frequency / 200);
        var d = new float[n * ch];
        if (!c.GetData(d, 0)) return c; // shortcut: needs "Decompress On Load" (the mp3 default); streamed clips stay as they are
        int windows = (n + win - 1) / win;
        var rms = new float[windows];
        float peak = 0, loudest = 0;
        for (int w = 0; w < windows; w++)
        {
            float sum = 0;
            int from = w * win * ch, to = Mathf.Min(n, (w + 1) * win) * ch;
            for (int i = from; i < to; i++) { sum += d[i] * d[i]; peak = Mathf.Max(peak, Mathf.Abs(d[i])); }
            rms[w] = Mathf.Sqrt(sum / (to - from));
            loudest = Mathf.Max(loudest, rms[w]);
        }
        if (loudest == 0) return null;
        int start = 0, end = windows - 1;
        while (rms[start] < 0.1f * loudest) start++;
        if (!keepTail) while (rms[end] < 0.06f * loudest) end--; // recorded hits (Ball/Cushion/Cue) come with their own faded tail
        int first = Mathf.Max(0, start * win - c.frequency / 1000), frames = Mathf.Min(n, (end + 1) * win) - first; // 1 ms before the attack
        if (frames > maxSeconds * c.frequency)
        {
            Debug.LogWarning($"[Sfx] {c.name}: still loud after {maxSeconds}s (noise), skipped");
            return null;
        }
        int fade = keepTail ? 1 : Mathf.Max(1, frames / 3); // the tail decays instead of stopping dead
        var o = new float[frames * ch];
        for (int f = 0; f < frames; f++)
            for (int k = 0; k < ch; k++)
                o[f * ch + k] = d[(first + f) * ch + k] * (0.9f / peak) * Mathf.Min(1, (frames - f) / (float)fade);
        var clip = AudioClip.Create(c.name, frames, ch, c.frequency, false);
        clip.SetData(o, 0);
        return clip;
    }
}
