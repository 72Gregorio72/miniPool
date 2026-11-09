using UnityEngine;

// Sound effects and background music from Resources/Audio (generated with ElevenLabs):
// one folder per kind (Ball, Cushion, Cue, Pocket) with any number of variants, picked at random, plus an optional <Kind>Soft folder
// for gentle hits (a soft hit sounds different, not just quieter: played quiet, a hard hit sounds far away). Music = looping lounge track.
// Hits are collected per frame and the loudest of each kind plays once, so a break doesn't stack up dozens of voices.
// shortcut: multiplayer clients don't run the physics, so they only hear their own cue; send hits in the state if it matters
public class Sfx : MonoBehaviour
{
    // Boost..Gate = modules (Land = a flying ball touching down); clips made by Tools/make_sfx.mjs
    public enum Kind { Ball, Cushion, Cue, Pocket, Boost, Fan, Land, Portal, Bumper, Manhole, Spikes, Magnet, Button, Gate }

    const float SfxVolume = 0.7f, MusicVolume = 0.3f;
    const float Soft = 0.25f;     // below this a hit uses the soft variants
    // seconds before the same kind plays again (unless louder): a cue ball with follow keeps nudging the ball it stopped behind,
    // and those micro-contacts played every few frames buzz like a broken TV
    const float Repeat = 0.15f;
    // seconds kept of each kind (by Kind): the generated clips trail off in hiss that sounds like a broken TV
    static readonly float[] MaxLength = { 0.15f, 0.25f, 0.15f, 0.5f, 0.55f, 0.55f, 0.3f, 0.65f, 0.4f, 0.45f, 0.45f, 0.4f, 0.2f, 0.55f };
    static Sfx inst;
    AudioSource source, music;
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
        source = gameObject.AddComponent<AudioSource>();
        if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

        var kinds = (Kind[])System.Enum.GetValues(typeof(Kind));
        clips = new AudioClip[kinds.Length][];
        softClips = new AudioClip[kinds.Length][];
        for (int i = 0; i < kinds.Length; i++)
        {
            clips[i] = Load("Audio/" + kinds[i], MaxLength[i]);
            softClips[i] = Load("Audio/" + kinds[i] + "Soft", MaxLength[i]);
            foreach (var c in clips[i]) Debug.Log($"[Sfx] kept {c.name} {c.length:F3}s"); // TEMP
            foreach (var c in softClips[i]) Debug.Log($"[Sfx] kept {c.name} {c.length:F3}s"); // TEMP
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
            if (s <= 0 || Time.time - lastAt[i] < Repeat && s <= lastVolume[i]) continue;
            bool soft = s < Soft && softClips[i].Length > 0;
            var set = soft ? softClips[i] : clips[i];
            if (set.Length == 0) continue;
            // each set covers its own range of strength: soft variants quiet, hard ones loud
            float lo = !soft && softClips[i].Length > 0 ? Soft : 0, hi = soft ? Soft : 1, t = Mathf.InverseLerp(lo, hi, s);
            float volume = soft ? Mathf.Lerp(0.2f, 0.6f, t) : Mathf.Lerp(lo > 0 ? 0.6f : 0.4f, 1, t);
            var clip = set[Random.Range(0, set.Length)];
            source.PlayOneShot(clip, volume * SfxVolume);
            Debug.Log($"[Sfx] {Time.time:F2} {clip.name} len {clip.length:F2}s strength {s:F2} volume {volume:F2}"); // TEMP: finding the noisy clip
            lastAt[i] = Time.time;
            lastVolume[i] = s;
        }
    }

    static AudioClip[] Load(string path, float maxSeconds) =>
        System.Array.FindAll(System.Array.ConvertAll(Resources.LoadAll<AudioClip>(path), c => Clean(c, maxSeconds)), c => c != null);

    // Trims by loudness over 5 ms windows: starts at the attack, ends where the sound drops below -24 dB of its loudest part
    // (the low hiss after it is cut), fades the end and brings every variant to the same peak, so they all match.
    // A clip still loud after `maxSeconds` has noise in it (sounds like a broken TV): skipped (null) with a warning.
    static AudioClip Clean(AudioClip c, float maxSeconds)
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
        while (rms[end] < 0.06f * loudest) end--;
        int first = Mathf.Max(0, start * win - c.frequency / 1000), frames = Mathf.Min(n, (end + 1) * win) - first; // 1 ms before the attack
        if (frames > maxSeconds * c.frequency)
        {
            Debug.LogWarning($"[Sfx] {c.name}: still loud after {maxSeconds}s (noise), skipped");
            return null;
        }
        int fade = Mathf.Max(1, frames / 3); // the tail decays instead of stopping dead
        var o = new float[frames * ch];
        for (int f = 0; f < frames; f++)
            for (int k = 0; k < ch; k++)
                o[f * ch + k] = d[(first + f) * ch + k] * (0.9f / peak) * Mathf.Min(1, (frames - f) / (float)fade);
        var clip = AudioClip.Create(c.name, frames, ch, c.frequency, false);
        clip.SetData(o, 0);
        return clip;
    }
}
