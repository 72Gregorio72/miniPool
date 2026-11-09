// Generates the module sound effects with the ElevenLabs Sound Effects API and trims them (silence/tail cut, peak normalized)
// before saving them as WAV in Assets/Resources/Audio/<Kind>/. Unity can stay open.
// Usage: node Tools/make_sfx.mjs [Kind ...]   (no args = all kinds; existing files of a kind are overwritten)
import { execSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";

const STYLE = "realistic arcade game sound effect, stylized, satisfying, clean and crisp, one single short dry hit, " +
  "no reverb, no echo, no music, no voice, no background noise, silence before and after";
// kind: [prompt, seconds asked, max seconds kept (longer is cut; over 3x = probably noise, regenerated)]
const KINDS = {
  Boost:   ["quick punchy speed boost whoosh with a bright rising zip, a ball shot forward", 0.6, 0.5],
  Fan:     ["springy air puff launch, a ball blown upward by a short fan gust", 0.6, 0.5],
  Land:    ["dull thud of a billiard ball landing on a felt table", 0.5, 0.25],
  Portal:  ["bright magical teleport warp blip, short sci-fi zap", 0.7, 0.6],
  Bumper:  ["pinball bumper hit, springy metallic pop", 0.5, 0.35],
  Manhole: ["metal clank of a ball dropping into a small metal cup", 0.5, 0.4],
  Spikes:  ["sharp metallic stab crunch, a ball impaled on spikes", 0.5, 0.4],
  Magnet:  ["magnetic clack, a steel ball snapping onto a magnet", 0.5, 0.35],
  Button:  ["plastic click of a pressed arcade button", 0.5, 0.15],
  Gate:    ["mechanical sliding thunk of a small stone gate moving", 0.7, 0.5],
};
const VARIANTS = 3, TRIES = 4, RATE = 44100;

const key = process.env.ELEVENLABS_API_KEY ||
  execSync("reg query HKCU\\Environment /v ELEVENLABS_API_KEY").toString().match(/REG_\w+\s+(\S+)/)[1];

async function generate(text, seconds) {
  const res = await fetch(`https://api.elevenlabs.io/v1/sound-generation?output_format=pcm_${RATE}`, {
    method: "POST",
    headers: { "xi-api-key": key, "Content-Type": "application/json" },
    body: JSON.stringify({ text, duration_seconds: seconds, prompt_influence: 0.7 }),
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

function wav(s) {
  const b = Buffer.alloc(44 + 2 * s.length);
  b.write("RIFF", 0); b.writeUInt32LE(36 + 2 * s.length, 4); b.write("WAVEfmt ", 8);
  b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(1, 22);
  b.writeUInt32LE(RATE, 24); b.writeUInt32LE(RATE * 2, 28); b.writeUInt16LE(2, 32); b.writeUInt16LE(16, 34);
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

for (const kind of process.argv.length > 2 ? process.argv.slice(2) : Object.keys(KINDS)) {
  const [prompt, seconds, max] = KINDS[kind];
  const dir = `Assets/Resources/Audio/${kind}`;
  mkdirSync(dir, { recursive: true });
  for (let v = 1; v <= VARIANTS; v++) {
    for (let t = 1; ; t++) {
      const raw = trim(await generate(`${prompt}, ${STYLE}`, seconds));
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
