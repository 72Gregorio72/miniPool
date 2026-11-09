# MiniPool — Specifica SFX e musica (rifacimento)

Autore: sound designer (con ruolo di audio direction, team individuale). Nessuna sound bible: la direzione sotto deriva dal brief e va considerata la bozza di palette.

## 1. Direzione sonora (palette)

- **Principio**: due famiglie nette.
  - **Fisiche (palla, sponda, stecca, buca, atterraggio, moduli)**: materiali reali, asciutti, vicini al microfono, nessun elemento synth. Il "low poly" lo dà la pulizia e la brevità, non l'effetto.
  - **Feedback (Click, Win, Fail, Crown, e in parte Boost/Portal)**: stilizzati, strumenti veri (marimba, glockenspiel, pizzicato), brevi, in tonalità con la musica (Do maggiore / La minore).
- **Perché ora suona male** (cause e rimedi applicati ai prompt):
  1. Le parole "arcade / stylized / satisfying" nei suoni fisici aggiungono zap e synth: tolte dai prompt fisici.
  2. Il suffisso lungo con molti "no ..." confonde il modello (le negazioni spesso lo portano a generare proprio quelle cose, e "silence before and after" lascia silenzio): sostituito da una sola frase positiva finale, diversa per famiglia ("close-miked, dry, studio-recorded single hit").
  3. Materiali generici ("rubbery thump") danno suoni molli: ora si nominano materiali veri (palla in resina fenolica, gomma K-66, ardesia sotto panno di lana, stecca in acero con punta di cuoio).
  4. Durata richiesta: l'API accetta minimo 0.5 s. Per i colpi brevi si chiede 0.5 s e il generatore taglia a `max`. Il file non deve contenere altro che il colpo.
- **Nessun suffisso aggiunto dal generatore**: i prompt sotto sono completi. `Tools/make_sfx.mjs` va aggiornato per non appendere più il suffisso comune (azione per chi implementa).
- **Generazione**: 3 chiamate separate per kind (non un solo prompt per 3 file) e scelta a orecchio dei migliori; scartare ogni risultato con coda/riverbero visibile. Con prompt_influence alto le varianti si somigliano: per variare, aggiungere un dettaglio diverso per variante (vedi colonna prompt: "A/B/C").

## 2. Tabella SFX

Colonne: kind | tenere/rifare | prompt completo | durata chiesta (s) | max tenuto (s) | prompt_influence | pitch random ± % | volume relativo 0..1

### 2.1 Fisici (famiglia "materiale reale")

| Kind | Tenere/Rifare | Prompt completo | Chiesta | Max | Infl. | Pitch | Vol |
|---|---|---|---|---|---|---|---|
| Ball | Rifare: mp3 di origine ignota, passata precedente; serve il "tac" cristallino della resina | A: "Single sharp click of two phenolic resin billiard balls colliding, bright crisp 'tock', hard glassy attack, very short decay, close-miked, dry, studio-recorded single hit" / B: stessa frase ma "slightly lower pitched, heavier click" / C: "slightly higher pitched, lighter click" | 0.5 | 0.15 | 0.8 | 4 | 0.85 |
| BallSoft | Rifare: deve essere un "tic" leggero, non un Ball abbassato | "Tiny soft tick of two billiard balls barely touching, light ceramic 'tik', quiet, almost no body, very short, close-miked, dry, studio-recorded single hit" | 0.5 | 0.12 | 0.8 | 4 | 0.5 |
| Cushion | Rifare: l'utente non lo gradisce; "rubbery thump" suona molle. Meglio il "thud" corto della gomma K-66 con un attacco secco della palla | A: "Short dull thud of a billiard ball hitting the firm rubber cushion of a pool table rail, tight low 'thup' with a tiny hard click at the start, no ring, no wood knock, close-miked, dry, studio-recorded single hit" / B: "... slightly deeper, more muted" / C: "... slightly sharper attack" | 0.5 | 0.2 | 0.85 | 4 | 0.75 |
| CushionSoft | Rifare: stesso motivo; qui un fruscio-tocco leggero, timbro diverso | "Very light muted pat of a slow billiard ball rolling into the rubber cushion of a pool table, soft 'pff' with no click, quiet, very short, close-miked, dry, studio-recorded single hit" | 0.5 | 0.18 | 0.8 | 4 | 0.45 |
| Cue | Rifare: mp3 di origine ignota; serve il colpo di cuoio su resina | A: "Sharp 'tick-tock' of a leather cue tip striking a billiard cue ball, crisp wooden-ceramic snap, tight and punchy, very short, close-miked, dry, studio-recorded single hit" / B: "... slightly deeper" / C: "... slightly brighter" | 0.5 | 0.15 | 0.8 | 3 | 0.8 |
| CueSoft | Rifare: tocco di stecca delicato (tiri deboli) | "Gentle soft tap of a leather cue tip nudging a billiard ball, small muted 'tp', quiet, very short, close-miked, dry, studio-recorded single hit" | 0.5 | 0.12 | 0.8 | 3 | 0.5 |
| Pocket | Rifare (tenere solo se l'orecchio lo giudica ok: valutare dopo A/B): serve "tonf" della palla + rotolio breve nella rete, non lungo | A: "Billiard ball dropping into a pool table pocket, quick leather-net 'thup' then a short low rumble of rolling inside, ends cleanly, close-miked, dry, studio-recorded single hit" / B e C: variare "slightly deeper" / "slightly shorter" | 1.0 | 0.5 | 0.75 | 3 | 0.8 |
| Land | Tenere il concetto, rifare il prompt: "dull thud" generico | "Soft heavy thump of a billiard ball landing on felt-covered slate, dull low 'dup' with a tiny click, no bounce, no ring, close-miked, dry, studio-recorded single hit" | 0.5 | 0.25 | 0.8 | 4 | 0.65 |

### 2.2 Moduli (fisici con carattere di gioco)

| Kind | Tenere/Rifare | Prompt completo | Chiesta | Max | Infl. | Pitch | Vol |
|---|---|---|---|---|---|---|---|
| Boost | Tenere con ritocco: whoosh+zip è leggibile; accorciare e togliere l'indeterminato "bright rising zip" | "Short fast whoosh with a quick rising air swish, like a ball shot forward along a track, punchy start, clean cutoff, dry, studio-recorded single sound" | 0.5 | 0.45 | 0.7 | 5 | 0.6 |
| Fan | Rifare: l'utente non lo gradisce; "whoosh/fwoosh" è rumore bianco. Meglio un soffio rotante + "pop" di lancio verso l'alto (due parti: raffica corta + piccolo rimbalzo d'aria) | A: "Quick upward burst of air from a small fan, a short 'fwuh' puff that rises in pitch, with a tiny soft pop at the start, like a ping pong ball blown upward, clean cutoff, dry, studio-recorded single sound" / B: "... slightly lower and heavier" / C: "... slightly lighter and quicker" | 0.5 | 0.4 | 0.7 | 5 | 0.6 |
| Portal | Rifare: "sci-fi zap" sporca; meglio due note di glockenspiel con glissando | "Short magical 'bloop-ding' with a quick upward pitch slide then a bright glockenspiel note, playful teleport sound, clean and tonal, dry, studio-recorded single sound" | 0.5 | 0.5 | 0.7 | 0 | 0.6 |
| Bumper | Tenere con ritocco: "pop metallico" ok, evitare il "molla" lungo | "Pinball bumper hit, a short bright metallic 'bonk' with a tiny spring boing, tight and punchy, dry, studio-recorded single sound" | 0.5 | 0.35 | 0.75 | 5 | 0.7 |
| Manhole | Tenere: clank corto, nessun commento | "Small metal ball dropping into a steel cup, single short hollow 'tink-clunk', no ring-out, close-miked, dry, studio-recorded single hit" | 0.5 | 0.4 | 0.75 | 4 | 0.65 |
| Spikes | Rifare: "crunch" tende a rumore; serve un colpo netto e leggibile come "morte" ma non macabro (gioco rilassato) | "Short sharp metallic 'shing-crack' of a ball hitting metal spikes then a quick cartoonish crunch, punchy, clean cutoff, dry, studio-recorded single sound" | 0.5 | 0.4 | 0.75 | 3 | 0.7 |
| Magnet | Tenere con ritocco | "Steel ball snapping onto a strong magnet, single short 'clack' with a slight metallic hum rise just before, tight, close-miked, dry, studio-recorded single hit" | 0.5 | 0.35 | 0.75 | 4 | 0.6 |
| Button | Tenere: breve; verificare solo che non abbia coda | "Single click of a pressed plastic arcade button, short hard 'tk-clk', close-miked, dry, studio-recorded single hit" | 0.5 | 0.15 | 0.8 | 3 | 0.6 |
| Gate | Rifare: "stone gate" produce rumble lungo e sporco; tagliato a 0.55 s suona mozzo. Meglio "thunk" meccanico corto, niente trascinamento | "Short heavy mechanical 'chunk-thud' of a small block sliding into place, solid low thunk with a quick grind at the start, clean cutoff, close-miked, dry, studio-recorded single sound" | 0.5 | 0.4 | 0.75 | 4 | 0.65 |

### 2.3 UI e feedback (famiglia "stilizzata, tonale")

Tonalità: Click resta legno/marimba; **Win, Fail e Crown** (rifatti il 2026-11-09, l'utente bocciava marimba/pizzicato) seguono il mondo della musica: organo vintage scordato, synth e piano elettrico su nastro. Nei prompt solo gesto e timbro, mai nomi di note (il modello non le esegue). Prompt completi (3 varianti A/B/C per Win e Fail) in `Tools/make_sfx.mjs`, analisi in `production/audio/jingle-vittoria-sconfitta/sfx-spec.md`. Gerarchia: Crown > Win > Fail.

| Kind | Tenere/Rifare | Prompt completo | Chiesta | Max | Infl. | Pitch | Vol |
|---|---|---|---|---|---|---|---|
| Click | Rifare: troppo generico. Un "tok" di legno/marimba corto, piacevole ripetuto | "Single short soft wooden 'tok' like a muted marimba note, clean and warm, UI button click, very short, dry, studio-recorded single sound" | 0.5 | 0.1 | 0.8 | 2 | 0.5 |
| Win | Rifatto: stinger "payout", accordo d'organo che sale come nastro che accelera + scintillio di piano elettrico, coda naturale. Varianti: A organo, B cascata di gettoni di vibrafono, C due colpi di synth | vedi `make_sfx.mjs` | 1.5 | 1.4 | 0.6 | 0 | 0.7 |
| Fail | Rifatto: "tape slip" morbido, una nota ovattata che si piega in giù; niente attacco secco, mai triste (si sente a ogni restart). Varianti: A organo, B bolla di synth, C gettone sul panno | vedi `make_sfx.mjs` | 0.8 | 0.7 | 0.6 | 0 | 0.45 |
| Crown | Rifatto: jackpot, come Win ma più grande (organo pieno, cascata di scintille, colpo finale, coda più lunga) | vedi `make_sfx.mjs` | 2.5 | 2.3 | 0.6 | 0 | 0.8 |

### 2.4 Varianti Soft

- Esistenti: BallSoft, CushionSoft, CueSoft (già in tabella).
- **Altre che meritano una Soft**: nessuna obbligatoria. Candidata: **Pocket** (palla lenta che cade nella buca = "plop" senza rotolio), da fare solo se dopo l'ascolto il Pocket normale suona troppo forte con tiri deboli; prompt: "Slow billiard ball dropping softly into a pool pocket, small muted 'plup', quiet, very short, close-miked, dry, studio-recorded single hit", chiesta 0.5, max 0.3, infl. 0.8, pitch 3, vol 0.5. Kind modulari: no (la forza non è variabile).
- Soglia soft 0.25 (`Sfx.Soft`): invariata.

### 2.5 Note di mix e codice

- Pitch random: un solo numero per kind come in tabella. Raccomandazione: attivarlo solo per le famiglie fisiche e moduli; 0 per Portal/Win/Fail/Crown (sono tonali).
- Volume relativo = moltiplicatore sul volume base per kind, prima della scala per forza d'urto. Sfx master 0.7 come oggi.
- Collisioni palla-palla fitte (break): il limite 0.15 s e "solo il più forte per frame" sono già corretti; non servono altri limiti.
- Il runtime scarta le clip ancora forti dopo `Max`: i `Max` sopra sono sopra la durata naturale del suono generato, quindi non dovrebbero tagliare il corpo. Se una clip viene scartata, abbassare `trim` o rigenerare, non alzare `Max`.
- Controllo qualità per file generato (da fare a orecchio, 3 secondi per file): attacco entro 10 ms dall'inizio; nessuna coda udibile dopo -30 dB; niente volume ondeggiante; nessun rumore di fondo.

## 3. Musica

### 3.1 Direzione

- **Genere** (scelto dall'utente il 2026-11-09, dopo ukulele/bossa, pianoforte da salotto e piano rilassante): **lounge psichedelica e ipnotica** nello stile delle colonne sonore dei roguelike di carte (riferimento di stile dell'utente: Balatro, da non copiare). Organo e synth vintage stonati che ondeggiano (wobble da nastro), basso funky rotondo, groove lento e costante, atmosfera fumosa da tavolo da gioco.
- **Tempo**: **96 bpm**: 12 battute in 4/4 durano esattamente 30.0 s, quindi il loop coincide con una frase musicale intera.
- **Tonalità**: La minore (relativa del Do maggiore dei jingle, quindi stanno bene insieme).
- **Strumenti**: organo elettrico vintage e synth detuned con vibrato, piano elettrico, basso funky, batteria morbida e rilassata.
- **Umore**: sole, tranquillo, giocoso ma non energico; sfondo, non protagonista. Melodia semplice che non si fissa in testa (note lunghe, poco cantabile) per non stancare.
- **Resistenza al loop**: dinamica costante, nessuna "build", nessun assolo, melodia poco riconoscibile, pianoforte nel registro medio-alto, lasciato libero sotto i 500 Hz per il "tac" delle palle (contrabbasso solo discreto).
- **Da evitare**: voce/coro (anche "ooh"), drop o crescendo, stacchi improvvisi, finale con ritardando o riverbero lungo (rovina il loop), batteria pesante o kick forte (copre i colpi), campane di Natale, fischi.

### 3.2 Prompt alternativi (loop: true, duration_seconds 30)

Come rendere più musicale l'output di ElevenLabs SFX: dare tutti i dati insieme (genere, bpm, tonalità, strumenti, struttura), usare la parola "loop" con "same energy from start to finish", escludere con frasi positive ("instrumental only" invece di "no vocals" ripetuto), e descrivere la **funzione** ("background music for a relaxing mini golf game"). Prompt_influence più alto (0.8) riduce la deriva stilistica. Generare 4–6 take e scegliere quella con inizio e fine più simili.

I prompt in uso sono in `Tools/make_sfx.mjs` (`MUSIC`): A = organo e synth wobbly con basso funky, B = synth pitch-bent e piano elettrico, groove shuffle, C = jazz-funk psichedelico con organo e synth col vibrato. Le prove escono in `production/audio/sfx-e-musica/music/` (stereo, 30 s, non tagliate); quella scelta va in `Assets/Resources/Audio/Music.wav`.

Parametri: loop true, duration_seconds 30, prompt_influence 0.8.

### 3.3 Volume e integrazione

- Volume musica raccomandato: **0.20–0.25** con SFX a 0.7 (oggi 0.3: troppo alto, copre i colpi delle palle). Slider utente resta.
- Se la presa scelta ha un piccolo "salto" al giro: chiedere al programmatore un crossfade di ~0.3 s a fine loop in `Sfx.cs` (due AudioSource alternate), oppure correggere in post con un taglio a battuta (non è possibile da qui, i file non li creo).
- Ducking: non esiste. Raccomandazione: abbassare la musica del 30% per 1.5 s quando suonano Win/Crown (richiede codice in `Sfx.cs`; non indispensabile).

## 4. Azioni per chi implementa (non fatte qui)

1. `Tools/make_sfx.mjs`: togliere il suffisso comune, usare prompt, durate, `max` e `prompt_influence` della tabella; supporto a 3 prompt per kind (A/B/C).
2. `Sfx.cs`: aggiornare `MaxLength` per kind, aggiungere moltiplicatore volume e pitch random per kind, music a 0.2–0.25.
3. Dopo la rigenerazione: ascolto di ogni kind in gioco (non solo nel file) con musica attiva, per verificare il mascheramento.

## 5. Rischi aperti

- Qualità ElevenLabs non garantita da nessun prompt: aspettarsi 2–3 giri di rigenerazione per Cushion, Fan, Spikes e Gate.
- La musica "30 s loop" può non chiudere in modo perfetto: il crossfade nel codice è il piano di riserva.
- Il suono di Pocket, Land, Ball e Cue può rivelarsi accettabile al primo ascolto: in quel caso tenerli e risparmiare crediti.
