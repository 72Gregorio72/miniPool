# MiniPool — cosa manca (in ordine)

- [x] **Audio** — `Sfx.cs`: urti palla-palla, sponde, stecca, buca (file ElevenLabs in `Resources/Audio`) + musica lounge in loop. Suoni dei moduli fatti (`Tools/make_sfx.mjs`). Da fare dopo: suoni online per i client, suoni continui (nastro, ghiaccio, sabbia, piattaforma).
- [x] **Menu pausa / impostazioni** — Esc in partita: riprendi, ricomincia, torna ai livelli; volume (`AudioListener.volume` in PlayerPrefs), sensibilità mouse/camera, fullscreen/risoluzione.
- [x] **Effetti ("juice")** — `Juice.cs`: cubetti del colore della palla in buca, palla che si disintegra sulle spine (PhysX), moduli che "rimbalzano" quando colpiti, shake sugli urti forti, slow-motion quando la nera punta dentro una buca (solo in singolo). Da tarare giocando (costanti in cima a `Juice.cs`).
- [x] **Rigiocabilità** — record (meno tiri in una partita, `GameManager.Best`) sotto ogni palla della selezione livelli; tiri minimi per livello (riga `tiri N`, campo "Tiri minimi" nell'editor) → corona = outline dorato sulla palla (`GameUI.CrownOutline`); skin sbloccabili (`BallArt.Skin.world` / `crowns`: Smeraldo = finisci il Mondo 1, Reale = corona in tutti i livelli del Mondo 1). Da fare: impostare i tiri minimi dei 15 livelli.
- [x] **Editor livelli in gioco** — home > "Crea livelli" (`LevelEditor`, `GameUI.Editor.cs`): schede Costruisci / Modifica / Elimina, tutti i pennelli, Sposta / Copia-Incolla, pile, piani, rotazione, annulla / ripeti, prova, salvataggio in `persistentDataPath/Livelli`, esporta / importa file sul PC (`FileDialog`). Obiettivo dei livelli utente: la 8.
- [ ] **Livelli segreti** — un livello extra per mondo, si sblocca con tutte le corone di quel mondo (come le skin `crowns`, `GameManager.WorldDone`).
- [ ] **Lingue** — italiano / inglese: testi di `GameUI` in una tabella, scelta nelle impostazioni (PlayerPrefs).
- [ ] Minori: gamepad, replay dell'ultimo tiro, sblocco progressivo dei mondi.
