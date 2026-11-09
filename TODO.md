# MiniPool — cosa manca (in ordine)

- [x] **Audio** — `Sfx.cs`: urti palla-palla, sponde, stecca, buca (file ElevenLabs in `Resources/Audio`) + musica lounge in loop. Suoni dei moduli fatti (`Tools/make_sfx.mjs`). Da fare dopo: suoni online per i client, suoni continui (nastro, ghiaccio, sabbia, piattaforma).
- [x] **Menu pausa / impostazioni** — Esc in partita: riprendi, ricomincia, torna ai livelli; volume (`AudioListener.volume` in PlayerPrefs), sensibilità mouse/camera, fullscreen/risoluzione.
- [ ] **Contenuti** — primo mondo fatto (15 livelli); riempire gli altri mondi: i moduli nuovi (respingente, portali, ghiaccio, sabbia, tombino, pulsante/cancello, muro a tempo, nastro, calamita, piattaforma) sono tenuti per il mondo 2.
- [x] **Effetti ("juice")** — `Juice.cs`: cubetti del colore della palla in buca, palla che si disintegra sulle spine (PhysX), moduli che "rimbalzano" quando colpiti, shake sugli urti forti, slow-motion quando la nera punta dentro una buca (solo in singolo). Da tarare giocando (costanti in cima a `Juice.cs`).
- [x] **Rigiocabilità** — record (meno tiri in una partita, `GameManager.Best`) sotto ogni palla della selezione livelli; tiri minimi per livello (riga `tiri N`, campo "Tiri minimi" nell'editor) → corona = outline dorato sulla palla (`GameUI.CrownOutline`); skin sbloccabili (`BallArt.Skin.world` / `crowns`: Smeraldo = finisci il Mondo 1, Reale = corona in tutti i livelli del Mondo 1). Da fare: impostare i tiri minimi dei 15 livelli.
- [ ] **Editor livelli in gioco** — oggi solo in Unity (`LevelEditorWindow`); portarlo nel gioco (uGUI da codice come `GameUI`): pennelli, piani, angoli, prova, salvataggio dei livelli dell'utente in `Application.persistentDataPath` (stesso formato `.txt`, letto da `LevelBuilder`).
- [ ] **Livelli segreti** — un livello extra per mondo, si sblocca con tutte le corone di quel mondo (come le skin `crowns`, `GameManager.WorldDone`).
- [ ] **Lingue** — italiano / inglese: testi di `GameUI` in una tabella, scelta nelle impostazioni (PlayerPrefs).
- [ ] Minori: gamepad, replay dell'ultimo tiro, sblocco progressivo dei mondi.
