// Generates every sound effect with the ElevenLabs Sound Effects API and trims them (silence/tail cut, peak normalized)
// before saving them as WAV in Assets/Resources/Audio/<Kind>/. Unity can stay open.
// Usage: node Tools/make_sfx.mjs [Kind ...]   (no args = all kinds; existing files of a kind are overwritten)
//        node Tools/make_sfx.mjs --music      (background music candidates, see MUSIC)
import { execSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";

// Prompts: production/audio/sfx-e-musica/sfx-spec.md. Each is complete (no shared suffix: a list of "no ..." makes the model add them).
// Physical sounds name the real materials and no "arcade" (it adds synthy zaps); UI is tonal, in C major like the music.
const HIT = "close-miked, dry, studio-recorded single hit", ONE = "dry, studio-recorded single sound";
const v3 = (base, a, b, c, end = HIT) => [a, b, c].map(x => `${base}${x}, ${end}`); // three takes that differ in one detail
// kind: [prompt or 3 prompts (one per variant), seconds asked (API min 0.5), max seconds kept (longer is cut; over 3x = noise, regenerated), prompt_influence]
const KINDS = {
  Ball:        [v3("Single sharp click of two phenolic resin billiard balls colliding, bright crisp 'tock', hard glassy attack, very short decay",
                 "", ", slightly lower pitched, heavier click", ", slightly higher pitched, lighter click"), 0.5, 0.15, 0.8],
  BallSoft:    [`Tiny soft tick of two billiard balls barely touching, light ceramic 'tik', quiet, almost no body, very short, ${HIT}`, 0.5, 0.12, 0.8],
  Cushion:     [v3("Short dull thud of a billiard ball hitting the firm rubber cushion of a pool table rail, tight low 'thup' with a tiny hard click at the start, no ring, no wood knock",
                 "", ", slightly deeper, more muted", ", slightly sharper attack"), 0.5, 0.2, 0.85],
  CushionSoft: [`Very light muted pat of a slow billiard ball rolling into the rubber cushion of a pool table, soft 'pff' with no click, quiet, very short, ${HIT}`, 0.5, 0.18, 0.8],
  Cue:         [v3("Sharp 'tick-tock' of a leather cue tip striking a billiard cue ball, crisp wooden-ceramic snap, tight and punchy, very short",
                 "", ", slightly deeper", ", slightly brighter"), 0.5, 0.15, 0.8],
  CueSoft:     [`Gentle soft tap of a leather cue tip nudging a billiard ball, small muted 'tp', quiet, very short, ${HIT}`, 0.5, 0.12, 0.8],
  Pocket:      [v3("Billiard ball dropping into a pool table pocket, quick leather-net 'thup' then a short low rumble of rolling inside, ends cleanly",
                 "", ", slightly deeper", ", slightly shorter"), 1.0, 0.5, 0.75],
  Land:        [`Soft heavy thump of a billiard ball landing on felt-covered slate, dull low 'dup' with a tiny click, no bounce, no ring, ${HIT}`, 0.5, 0.25, 0.8],
  Boost:       [`Short fast whoosh with a quick rising air swish, like a ball shot forward along a track, punchy start, clean cutoff, ${ONE}`, 0.5, 0.45, 0.7],
  Fan:         [v3("Quick upward burst of air from a small fan, a short 'fwuh' puff that rises in pitch, with a tiny soft pop at the start, like a ping pong ball blown upward, clean cutoff",
                 "", ", slightly lower and heavier", ", slightly lighter and quicker", ONE), 0.5, 0.4, 0.7],
  Portal:      [`Short magical 'bloop-ding' with a quick upward pitch slide then a bright glockenspiel note, playful teleport sound, clean and tonal, ${ONE}`, 0.5, 0.5, 0.7],
  Bumper:      [`Pinball bumper hit, a short bright metallic 'bonk' with a tiny spring boing, tight and punchy, ${ONE}`, 0.5, 0.35, 0.75],
  Manhole:     [`Small metal ball dropping into a steel cup, single short hollow 'tink-clunk', no ring-out, ${HIT}`, 0.5, 0.4, 0.75],
  Spikes:      [`Short sharp metallic 'shing-crack' of a ball hitting metal spikes then a quick cartoonish crunch, punchy, clean cutoff, ${ONE}`, 0.5, 0.4, 0.75],
  Magnet:      [`Steel ball snapping onto a strong magnet, single short 'clack' with a slight metallic hum rise just before, tight, ${HIT}`, 0.5, 0.35, 0.75],
  Button:      [`Single click of a pressed plastic arcade button, short hard 'tk-clk', ${HIT}`, 0.5, 0.15, 0.8],
  Gate:        [`Short heavy mechanical 'chunk-thud' of a small block sliding into place, solid low thunk with a quick grind at the start, clean cutoff, close-miked, ${ONE}`, 0.5, 0.4, 0.75],
  Click:       [`Single short soft wooden 'tok' like a muted marimba note, clean and warm, UI button click, very short, ${ONE}`, 0.5, 0.1, 0.8],
  // jingles in the music's world (vintage organ, tape wobble): gestures, never note names (the model can't play exact notes);
  // production/audio/jingle-vittoria-sconfitta/sfx-spec.md
  Win:         [["Short victory stinger for an arcade card-table game: a warm detuned vintage electric organ chord swells upward with a quick tape-speed-up pitch glide, lands on a bright full major-sounding chord, then a small shimmer of electric piano bell sparkles rings out and fades naturally. Wobbly analog tape saturation, lo-fi warm, playful and satisfying, single phrase, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly.",
                 "Short satisfying casino payout sound in a vintage lounge style: a quick cascade of warm vibraphone and electric piano sparkles rising upward like coins landing, then a soft detuned organ stab as the final accent, tape-saturated and slightly wobbly, bright and rewarding, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly.",
                 "Short triumphant two-hit synth stinger: a first soft wobbly analog synth stab, then a second brighter higher stab with a quick rising pitch slide, vintage tape-saturated lo-fi texture, a little detuned chorus shimmer, cheerful and cool laid-back, a gentle natural decay at the end, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly."], 1.5, 1.4, 0.6],
  // heard on every restart: soft, never sad or sharp
  Fail:        [["Very short soft 'oops' sound: a single muted vintage organ note whose pitch sags downward quickly like a tape machine slowing for a moment, warm wobbly lo-fi analog texture, gentle round attack with no sharp click, light friendly and slightly humorous, not sad, short natural fade, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly.",
                 "Very short gentle soft synth blip that bends downward and deflates like a small bubble, warm tape-saturated analog tone, slightly detuned wobble, soft rounded attack, mellow and cheeky, not sad, not harsh, quick natural fade out, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly.",
                 "Very short soft felt-muffled two-note downward sigh from an electric piano with tape wobble, quiet and mellow, like a casino chip dropping onto a felt table with a tiny warm tone, no sharp attack, friendly and neutral, not sad, quick natural fade, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly."], 0.8, 0.7, 0.6],
  // bigger than Win: the jackpot
  // (API limit: 450 characters per prompt)
  Crown:       ["Triumphant golden jackpot stinger: a big warm detuned vintage organ chord swells upward with a tape-speed-up pitch glide, a rich cascade of bright vibraphone and electric piano sparkles shimmers above it, a final grand warm stab, then a glittering natural decay. Wobbly analog tape saturation, lo-fi warm, joyful, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly.", 2.5, 2.3, 0.6],
};
// background music candidates (node Tools/make_sfx.mjs --music): 30 s seamless loops (96 bpm = 12 bars), untrimmed WAV in MUSIC_DIR;
// the chosen one is copied to Assets/Resources/Audio/Music.wav
const MUSIC = {
  // hypnotic psychedelic lounge: wobbly detuned vintage keys, funky round bass, steady laid-back groove; 96 bpm = 12 bars in 30 s
  A: "Hypnotic psychedelic lounge loop, warbly detuned vintage electric organ and a wobbly tape-saturated synth playing a slow dreamy minor melody, groovy funky bass line, laid-back steady drum groove, smoky card table atmosphere, 96 bpm, A minor, lo-fi warm analog sound, relaxing and satisfying, constant energy, seamless loop, instrumental only",
  B: "Woozy retro lounge groove loop, wobbling pitch-bent synth lead and warm electric piano chords, round bouncy bass guitar, soft shuffling drums, mysterious chill card game vibe, 96 bpm, A minor, vintage tape wobble, mellow and hypnotic, seamless loop, instrumental only",
  C: "Dreamy psychedelic jazz-funk loop, detuned warbling organ chords, slow gliding synth melody with heavy vibrato, deep groovy bass, relaxed head-nodding drum beat, cozy smoky lounge, 96 bpm, A minor, lo-fi analog warmth, steady and soothing, seamless loop, instrumental only",
};
const MUSIC_DIR = "production/audio/sfx-e-musica/music", MUSIC_TAKES = 2;
const VARIANTS = 3, TRIES = 4, RATE = 44100;

const key = process.env.ELEVENLABS_API_KEY ||
  execSync("reg query HKCU\\Environment /v ELEVENLABS_API_KEY").toString().match(/REG_\w+\s+(\S+)/)[1];

async function generate(text, seconds, influence, loop = false) {
  const res = await fetch(`https://api.elevenlabs.io/v1/sound-generation?output_format=pcm_${RATE}`, {
    method: "POST",
    headers: { "xi-api-key": key, "Content-Type": "application/json" },
    body: JSON.stringify({ text, duration_seconds: seconds, prompt_influence: influence, loop }),
  });
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`);
  const buf = Buffer.from(await res.arrayBuffer());
  const s = new Float32Array(buf.length >> 1);
  for (let i = 0; i < s.length; i++) s[i] = buf.readInt16LE(2 * i) / 32768;
  return s;
}

// cut by loudness over 5 ms windows: from 2 ms before the attack to where it drops below 3% of the loudest window,
// cut at maxSeconds if still going (fade over its last third, else 15 ms), peak to 0.9. Returns null if silent.
function trim(s, maxSeconds = Infinity) {
  const win = Math.floor(RATE / 200), n = Math.ceil(s.length / win), rms = new Float32Array(n);
  let loud = 0, peak = 0;
  for (let w = 0; w < n; w++) {
    let sum = 0, to = Math.min(s.length, (w + 1) * win);
    for (let i = w * win; i < to; i++) { sum += s[i] * s[i]; peak = Math.max(peak, Math.abs(s[i])); }
    rms[w] = Math.sqrt(sum / (to - w * win));
    loud = Math.max(loud, rms[w]);
  }
  if (loud === 0) return null;
  let a = 0, b = n - 1;
  while (rms[a] < 0.1 * loud) a++;
  while (rms[b] < 0.03 * loud) b--;
  const from = Math.max(0, a * win - RATE / 500), end = Math.min(s.length, (b + 1) * win);
  const to = Math.min(end, from + Math.floor(maxSeconds * RATE)), fade = to < end ? (to - from) / 3 : RATE * 0.015;
  const o = s.slice(from, to);
  for (let i = 0; i < o.length; i++) o[i] *= (0.9 / peak) * Math.min(1, (o.length - i) / fade);
  return o;
}

function wav(s, ch = 1) {
  const b = Buffer.alloc(44 + 2 * s.length);
  b.write("RIFF", 0); b.writeUInt32LE(36 + 2 * s.length, 4); b.write("WAVEfmt ", 8);
  b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(ch, 22);
  b.writeUInt32LE(RATE, 24); b.writeUInt32LE(RATE * 2 * ch, 28); b.writeUInt16LE(2 * ch, 32); b.writeUInt16LE(16, 34);
  b.write("data", 36); b.writeUInt32LE(2 * s.length, 40);
  for (let i = 0; i < s.length; i++) b.writeInt16LE(Math.round(Math.max(-1, Math.min(1, s[i])) * 32767), 44 + 2 * i);
  return b;
}

// self-check of the trim: 100 ms silence + 50 ms tone + 200 ms silence -> about 52 ms kept, peak 0.9
{
  const t = new Float32Array(RATE * 0.35);
  for (let i = RATE * 0.1; i < RATE * 0.15; i++) t[i] = 0.5 * Math.sin(i / 5);
  const o = trim(t), ms = o.length / RATE * 1000;
  if (ms < 50 || ms > 60 || Math.abs(Math.max(...o.map(Math.abs)) - 0.9) > 0.01) throw new Error(`trim self-check failed: ${ms} ms`);
}

if (process.argv[2] === "--music") {
  mkdirSync(MUSIC_DIR, { recursive: true });
  for (const [name, prompt] of Object.entries(MUSIC))
    for (let t = 1; t <= MUSIC_TAKES; t++) {
      writeFileSync(`${MUSIC_DIR}/music-${name}${t}.wav`, wav(await generate(prompt, 30, 0.8, true), 2)); // a loop: never trimmed; music comes back stereo (sfx mono)
      console.log(`music ${name}${t}`);
    }
  process.exit(0);
}

for (const kind of process.argv.length > 2 ? process.argv.slice(2) : Object.keys(KINDS)) {
  const [prompts, seconds, max, influence] = KINDS[kind];
  const dir = `Assets/Resources/Audio/${kind}`;
  mkdirSync(dir, { recursive: true });
  for (let v = 1; v <= VARIANTS; v++) {
    for (let t = 1; ; t++) {
      const raw = trim(await generate(Array.isArray(prompts) ? prompts[v - 1] : prompts, seconds, influence));
      const len = raw ? raw.length / RATE : Infinity;
      if (len <= 3 * max) {
        const o = trim(raw, max);
        writeFileSync(`${dir}/${kind.toLowerCase()}${v}.wav`, wav(o));
        console.log(`${kind} ${v}: ${len.toFixed(3)} s${len > max ? ` cut to ${max}` : ""}`);
        break;
      }
      console.log(`${kind} ${v}: ${len.toFixed(3)} s > ${3 * max}, retry`);
      if (t === TRIES) { console.log(`${kind} ${v}: skipped`); break; }
    }
  }
}
