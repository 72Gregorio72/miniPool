# MiniPool — cosa manca (in ordine)

- [x] **Audio** — `Sfx.cs`: urti palla-palla, sponde, stecca, buca (file ElevenLabs in `Resources/Audio`) + musica lounge in loop. Suoni dei moduli fatti (`Tools/make_sfx.mjs`). Da fare dopo: suoni online per i client, suoni continui (nastro, ghiaccio, sabbia, piattaforma).
- [x] **Menu pausa / impostazioni** — Esc in partita: riprendi, ricomincia, torna ai livelli; volume (`AudioListener.volume` in PlayerPrefs), sensibilità mouse/camera, fullscreen/risoluzione.
- [x] **Effetti ("juice")** — `Juice.cs`: cubetti del colore della palla in buca, palla che si disintegra sulle spine (PhysX), moduli che "rimbalzano" quando colpiti, shake sugli urti forti, slow-motion quando la nera punta dentro una buca (solo in singolo). Da tarare giocando (costanti in cima a `Juice.cs`).
- [x] **Rigiocabilità** — record (meno tiri in una partita, `GameManager.Best`) sotto ogni palla della selezione livelli; tiri minimi per livello (riga `tiri N`, campo "Tiri minimi" nell'editor) → corona = outline dorato sulla palla (`GameUI.CrownOutline`); skin sbloccabili (`BallArt.Skin.world` / `crowns`: Smeraldo = finisci il Mondo 1, Reale = corona in tutti i livelli del Mondo 1). Da fare: impostare i tiri minimi dei 15 livelli.
- [x] **Editor livelli in gioco** — home > "Crea livelli" (`LevelEditor`, `GameUI.Editor.cs`): schede Costruisci / Modifica / Elimina, tutti i pennelli, Sposta / Copia-Incolla, pile, piani, rotazione, annulla / ripeti, prova, salvataggio in `persistentDataPath/Livelli`, esporta / importa file sul PC (`FileDialog`). Obiettivo dei livelli utente: la 8.
- [ ] **Livelli segreti** — un livello extra per mondo, si sblocca con tutte le corone di quel mondo (come le skin `crowns`, `GameManager.WorldDone`).
- [ ] **Lingue** — italiano / inglese: testi di `GameUI` in una tabella, scelta nelle impostazioni (PlayerPrefs).
- [ ] Minori: gamepad, replay dell'ultimo tiro, sblocco progressivo dei mondi.

## Da sistemare (revisione del 2026-11-10, in ordine)

- [x] **Fisica: niente LINQ nel ciclo a 1 ms** — `Level.PocketAt` e `PlatformAt` sono cicli `for`.
- [x] **HUD di partita da IMGUI a uGUI** — `GameUI.SetHud` (riga in alto, messaggio, comandi) e `GameUI.SetCue` (effetto, stecca alzata, barra potenza) nel canvas; `par` letto una volta al caricamento. Da verificare a occhio: posizioni e dimensioni.
- [x] **`GameManager` testabile** — `Rules.Run` (tiri, tentativi, falli, `EndShot`) + `RulesTests.Run_…`; `startSpot` (posizioni) resta in `GameManager`.
- [x] **Rete: difese sui messaggi** — `OnMessage` fa try/catch intorno a `Handle`: l'host espelle il mittente, il client esce con messaggio; `Protocol` nel `Hello`; `return` dopo il `catch` in `Join`.
- [x] **Font** — se nessun font di sistema della lista esiste, `GameUI` usa `LegacyRuntime.ttf` (Liberation Sans, incluso in ogni build): nessun TTF da aggiungere.
- [x] **Anteprima dell'editor** — l'anteprima col pennello si ricostruisce dopo 50 ms di mouse fermo (`HoverDelay`, in gioco e in `LevelEditorWindow`); il riquadro segue subito.
- [x] **`PlayerPrefs.Save()` nei setter** — skin tavolo, hard mode, volume, sensibilità, musica.
- [x] **`Sfx.Clean` all'avvio** — i clip si caricano e puliscono un tipo per frame (`Sfx.LoadClips`, ~18 frame), niente hitch; i WAV restano come sono (nessun post-processor: ne servirebbe uno anche per i file saltati perché rumorosi).
- [x] **Layout di `GameUI`** — `GameUI.Bar` (`HorizontalLayoutGroup`) per le barre di pulsanti: risultati, multiplayer, lista livelli utente (riga e pulsanti in alto), barra in alto dell'editor. Il resto resta in coordinate.
- [x] **`PoolPhysics.Predict` online** — orizzonte da 3 a 2 s (max 1000 step). Non misurato: se la mira online scatta ancora, alzare `dt` o la soglia dei 50 ms in `UpdateGuide`.
- [x] **level30**: palla 15 al posto della 3.

spiegare camera al livello 2
mettere x per rimuovere i testi nei primi livelli

