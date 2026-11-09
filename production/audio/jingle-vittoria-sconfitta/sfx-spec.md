# SFX Spec — Jingle Vittoria (Win) e Sconfitta (Fail)

Autore: sound designer (copre anche la direzione audio, nessuna sound bible esistente). Stato: proposta, da scegliere A/B/C dall'utente.
Mondo sonoro di riferimento: lounge psichedelica ipnotica, organo elettrico vintage scordato/ondeggiante, synth saturo su nastro, basso funky rotondo, 96 bpm, La minore, lo-fi analogico caldo, tavolo da carte fumoso.

Regole di progetto rispettate (CLAUDE.md, `Sfx`): solo il suono nel file, niente musica/rumore/riverbero prima o dopo; corto e secco; stile realistico arcade che sta bene col low poly. Nota: "secco" = niente coda di ambiente. Una coda naturale breve dello strumento (decadimento) e' ammessa, e per Win serve.

## Principi comuni

- Il modello SFX di ElevenLabs non suona note precise: descrivere gesto e timbro (sale, cade, ondeggia, pagamento), mai nomi di note. Il risultato sara' in una tonalita' casuale: per questo gli stinger sono pensati come gesti (glissando, sag di nastro) che reggono con qualunque altezza sopra un loop in La minore. Rischio residuo: stonare col loop; mitigato dal timbro scordato/wobbly (tollera la dissonanza) e dalla durata corta.
- Vecchio marimba/pizzicato: timbri acustici, secchi, "da cartone animato", estranei al nuovo mondo organo/nastro.
- Il taglio a max secondi con fade sull'ultimo terzo mangiava la chiusura del jingle: ora si chiede una durata di poco sopra il max e si tiene una coda naturale.
- Il "dry/studio-recorded" tolto: una ricompensa ha bisogno di brillantezza. Si chiede "bright" ma "no reverb tail" per restare puliti.
- Fail e' sentito decine di volte: niente tonalita' triste (discesa minore, trombone), niente transiente duro; deve sembrare un "tape slip" morbido, quasi un respiro del nastro, non una punizione.

---

## WIN

### (a) Cosa deve essere e perche' il vecchio e' fallito

Uno stinger da "payout" da tavolo da gioco: un accordo pieno di organo vintage scordato che sale con un breve glissando (come un nastro che prende velocita'), cade su un'accordata calda e brillante, con uno scintillio di campanelle da piano elettrico/vibrafono che si chiude con una coda naturale di ~0.4 s. Deve dare soddisfazione e lasciare spazio a Crown (piu' corto, meno strati, meno scintillio, meno ampiezza). Il vecchio e' fallito perche': (1) l'arpeggio C E G C a note esatte non viene eseguito dal modello, esce poltiglia di marimba; (2) marimba/glockenspiel sono in un altro mondo rispetto a organo e nastro; (3) tagliato a 1.1 s da 1.5 chiesti, la fine veniva mutilata dal fade; (4) "dry" ha tolto lo scintillio che una vittoria richiede.

### (b) Prompt (3 varianti)

**Win A — organo che sale + scintillio (consigliata)**
"Short victory stinger for an arcade card-table game: a warm detuned vintage electric organ chord swells upward with a quick tape-speed-up pitch glide, lands on a bright full major-sounding chord, then a small shimmer of electric piano bell sparkles rings out and fades naturally. Wobbly analog tape saturation, lo-fi warm, playful and satisfying, single phrase, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly."

**Win B — payout da casino/slot, tono caldo**
"Short satisfying casino payout sound in a vintage lounge style: a quick cascade of warm vibraphone and electric piano sparkles rising upward like coins landing, then a soft detuned organ stab as the final accent, tape-saturated and slightly wobbly, bright and rewarding, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly."

**Win C — sintetizzatore a nastro, due colpi**
"Short triumphant two-hit synth stinger: a first soft wobbly analog synth stab, then a second brighter higher stab with a quick rising pitch slide, vintage tape-saturated lo-fi texture, a little detuned chorus shimmer, cheerful and cool laid-back, a gentle natural decay at the end, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly."

### (c) Parametri

| Parametro | Valore |
|---|---|
| `duration_seconds` richiesti | 1.5 |
| Max secondi tenuti | 1.4 (coda naturale; il vecchio 1.1 troncava) |
| `prompt_influence` | 0.6 (piu' alto del default per ridurre la deriva verso musica/arpeggi) |
| Gain | 0.7 (era 0.8; il nuovo timbro e' piu' pieno e brillante, e Crown deve restare sopra) |

Il rigenera-se-piu'-lungo-di-3x-max non scatta (1.5 << 4.2). Controllare all'ascolto che la soglia 3% non tagli lo scintillio finale: se succede, alzare il max a 1.6.

---

## FAIL

### (a) Cosa deve essere e perche' il vecchio e' fallito

Un "tape slip" morbido: una nota breve di organo/synth ovattata che si piega verso il basso come un nastro che rallenta un istante, attacco senza spigoli, ~0.4 s, tono neutro-divertito ("ops, riprova"), mai triste. Deve stare sotto la musica e sparire in fretta, perche' si sente a ogni restart. Il vecchio e' fallito perche': (1) due note pizzicato G poi E richieste per nome non vengono rispettate; (2) il pizzicato ha un transiente secco che ripetuto decine di volte diventa fastidioso; (3) la coppia discendente suona come "sbagliato", punitivo, proprio il contrario dell'intento; (4) timbro acustico estraneo al nuovo mondo.

### (b) Prompt (3 varianti)

**Fail A — tape slip dell'organo (consigliata)**
"Very short soft 'oops' sound: a single muted vintage organ note whose pitch sags downward quickly like a tape machine slowing for a moment, warm wobbly lo-fi analog texture, gentle round attack with no sharp click, light friendly and slightly humorous, not sad, short natural fade, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly."

**Fail B — bolla di synth che si sgonfia**
"Very short gentle soft synth blip that bends downward and deflates like a small bubble, warm tape-saturated analog tone, slightly detuned wobble, soft rounded attack, mellow and cheeky, not sad, not harsh, quick natural fade out, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly."

**Fail C — thud morbido da gettone**
"Very short soft felt-muffled two-note downward sigh from an electric piano with tape wobble, quiet and mellow, like a casino chip dropping onto a felt table with a tiny warm tone, no sharp attack, friendly and neutral, not sad, quick natural fade, no drums, no bass, no vocals, no reverb tail, no background noise, ends cleanly."

### (c) Parametri

| Parametro | Valore |
|---|---|
| `duration_seconds` richiesti | 0.8 (minimo API 0.5; 0.8 lascia al modello la coda naturale) |
| Max secondi tenuti | 0.7 (era 0.6 con taglio netto) |
| `prompt_influence` | 0.6 |
| Gain | 0.45 (era 0.6: frequenza d'ascolto altissima, timbro piu' morbido, deve restare gradevole alla ventesima volta) |

Fail C e' la piu' lontana dal vocabolario organo/nastro: tenerla come variante di sicurezza se A/B escono troppo "synth".

---

## CROWN: rifarlo?

**Si'**, perche' il nuovo Win e' piu' pieno e brillante e Crown deve restare chiaramente sopra (piu' lungo, piu' strati, "dorato"), altrimenti non sembra piu' grande. Il file attuale non e' stato letto: se resta marimba/glockenspiel e' comunque fuori mondo.

Prompt Crown:
"Triumphant golden reward stinger for an arcade card-table game: a big warm detuned vintage electric organ chord swells upward with a tape-speed-up pitch glide, a rich cascade of bright vibraphone and electric piano sparkles rises and shimmers above it like a jackpot payout, a final grand warm stab, then a longer glittering natural decay. Wobbly analog tape saturation, lo-fi warm, joyful and celebratory, no drums, no bass, no vocals, no reverb tail beyond the natural decay, no background noise, ends cleanly."

Parametri Crown: `duration_seconds` 2.5, max 2.3, `prompt_influence` 0.6, gain 0.8 (resta sopra Win 0.7 sia per durata sia per livello).

## Mix

Musica 0.22, master sfx 0.7. Win/Crown/Fail passano per lo stesso master. Le gerarchie di livello: Crown (0.8) > Win (0.7) > Fail (0.45). Nessun ducking della musica richiesto; se Win viene coperto dal basso funky del loop, valutare un breve duck musicale in `Sfx` (da far valutare al programmatore che ne e' responsabile, non e' deciso qui).

## Procedura di scelta

Generare le 3 varianti A/B/C per ciascuno (script `Tools/make_sfx.mjs`, un prompt per variante), ascoltare in gioco sopra la nuova musica a volume reale, tenere la migliore e scartare le altre. Se Fail non e' tollerabile dopo ~30 restart consecutivi, abbassare il gain a 0.35 prima di rigenerare.
