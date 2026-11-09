# Mondo 2 — Layout dei livelli 16–30

Convenzioni: colonne `x` da sinistra (0), righe `z` dal basso (0). Sub = 3 × tile. Ogni griglia è stata contata a mano (righe di pari lunghezza). Dove la riga angolo manca, l'angolo è 0 (verso il lato lontano). Tutti i livelli hanno la buca come singola `O` nell'anello di sponde con panno davanti (buca laterale).

Verificato nel codice: sponda del respingente raggio 0.3 (due respingenti su tile adiacenti formano un muro, centro a centro 2 = varco 1.4); muro a tempo e cancello alti 1; piattaforma `H`: `range` = numero di spazi consecutivi davanti, oscilla tra la cella `H` e l'ultimo spazio (la cella dopo deve essere panno); nastro e calamita agiscono anche su palle ferme; portale: velocità ruotata di `angolo(arrivo) − angolo(partenza)`, in senso orario visto dall'alto (destra + 90 = giù, destra + 270 = su); calamita: tira sempre almeno 12 dentro 2.5 tile, aggancia sotto 0.12 e 1.5 di velocità.

## 1. Direzione visiva e tema

Il tema "Oceano" va cambiato: panno blu (0.1, 0.42, 0.78) quasi uguale al portale blu (0.15, 0.55, 1) e vicino a ghiaccio, e tutto il mondo sarebbe un tono solo. Proposta: **"Laguna"**, panno verde acqua scuro, sponde più scure, legno crema e sabbia calda intorno: mood tropicale, luminoso, rilassato (lounge music già in gioco), con i moduli (giallo, rosa, rosso, viola, arancio, blu, ghiaccio chiaro) che risaltano per tinta e per luminosità contro un panno medio-scuro.

| Campo | RGB |
|-------|-----|
| felt | (0.10, 0.45, 0.48) |
| cushion | (0.07, 0.33, 0.36) |
| wood | (0.93, 0.88, 0.78) |
| darkWood | (0.50, 0.40, 0.30) |
| room (sabbia) | (0.96, 0.80, 0.55) |
| sky | (0.55, 0.82, 0.95) |
| accent | (1.00, 0.62, 0.25) |

Contrasto (palette in `LevelBuilder.cs`): portale blu (hue 214°, V 1.0) vs panno (hue 183°, V 0.48) ok; arancio, giallo cancello, rosa respingente, rosso pulsante/calamita, viola muro a tempo: tinte lontane. Ghiaccio (0.75, 0.92, 1) e sabbia (0.93, 0.8, 0.5) molto più chiari. Nastro (0.2, 0.22, 0.26) più scuro del panno. Piattaforma verde (0.35, 0.75, 0.6): più chiara del panno e sta sul vuoto. Da guardare in gioco: piattaforma vs panno (stessa famiglia), da verificare con screenshot.

## 2. Progressione

| Liv | Palla | Nome | Moduli | Ruolo | Diff | Par |
|----|----|------|--------|-------|----|----|
| 16 | 1 | Imbuto | Q | intro | 1 | 1 |
| 17 | 2 | Tunnel di gomma | Q | intro 2 / combo | 2 | 2 |
| 18 | 3 | Portale | T Y (0°) | intro | 1 | 1 |
| 19 | 4 | Curva | T Y (+90°) | intro 2 | 2 | 1 |
| 20 | 5 | Pista di ghiaccio | I | intro | 2 | 2 |
| 21 | 6 | Duna | M | intro | 2 | 1 |
| 22 | 7 | Tombino | W | intro | 2 | 2 |
| 23 | 8 | Corrente | N | intro | 3 | 2 |
| 24 | 9 | Cancello | K G (+ N già noto) | intro | 3 | 2 |
| 25 | 10 | Doppio varco | L | intro | 3 | 2 |
| 26 | 11 | Ponte mobile | H | intro | 2 | 2 |
| 27 | 12 | Calamita | Z | intro | 3 | 2 |
| 28 | 13 | Specchio di ghiaccio | T Y + I | combo | 4 | 2 |
| 29 | 14 | Catena di montaggio | N K G L | combo | 4 | 3 |
| 30 | 15 | Gran finale | H Z Q | combo | 5 | 3 |

Ordine di introduzione: Q, T/Y, I, M, W, N, K+G (il nastro, già visto in 23, serve solo da alimentatore), L, H, Z; poi 3 combinazioni. Nota: il 24 non è puro (usa N), ma N è già appreso e rende l'alimentazione del pulsante affidabile (una palla ferma sul nastro arriva sempre sul pulsante).

---

## Livello 16 — Imbuto (Q)

**Intento:** primo respingente. Due pali stringono la strada della buca (varco 1.4 tile): tiro dritto passa, un errore sfiora il palo e viene calciato (+6). **Soluzione:** 1 tiro dritto, bianca (2,3) sulla 8 (8,3), potenza media. **Errori:** tiro tagliato = la 8 sfiora un palo e schizza via veloce; bianca che segue nel varco (tiro con effetto alto) può finire in buca = restart.

```
###############
#.............#
#...........Q.#
#.............O
#...........Q.#
#.............#
###############
---
0 6 9
8 24 9
tiri 1
```

## Livello 17 — Tunnel di gomma (Q)

**Intento:** muro di pali = tunnel, la 8 deve entrarci dritta; ogni sfioramento aggiunge velocità e la rimette al centro. **Soluzione:** 1 tiro dritto lungo z=3 (bianca (2,3), 8 (5,3)); la 8 attraversa tutto il tunnel e va in buca (16,3). Par 2 per concedere un tentativo. **Errori:** tiro storto = la 8 rimbalza tra i pali accelerando (fino a ~30) e può uscire dal lato (le file z=1 e z=5 sono libere) o tornare indietro.

```
#################
#...............#
#.....QQQQQQ....#
#...............O
#.....QQQQQQ....#
#...............#
#################
---
0 6 9
8 15 9
tiri 2
```

## Livello 18 — Portale (T, Y)

**Intento:** muro pieno al centro, unica via il portale. **Soluzione:** bianca (2,3) dritta nel blu (4,3); esce dall'arancione (10,3) verso destra e colpisce la 8 (12,3), che va in buca (14,3). Angoli entrambi 0: nessuna rotazione. **Errori:** tiro fuori dal portale = batte sul muro, nessuna palla colpita = restart.

```
###############
#......#......#
#......#......#
#...T..#..Y...O
#......#......#
#......#......#
###############
---
> 4 3 0
> 10 3 0
0 6 9
8 36 9
tiri 1
```

## Livello 19 — Curva (T, Y ruotato)

**Intento:** il portale arancione ha angolo 90: la velocità ruota di +90° (destra diventa giù). **Soluzione:** bianca (2,3) dritta nel blu (4,3); esce da (10,5) verso il basso, colpisce la 8 (10,2) che scende in buca (10,0). **Errori:** pensare che esca a destra: la palla va in sponda e non colpisce. Un ingresso decentrato si sposta (offset ruotato, max 0.45), ma resta dentro il tile.

```
###############
#......#..Y...#
#......#......#
#...T..#......#
#......#......#
#......#......#
##########O####
---
> 4 3 0
> 10 5 90
0 6 9
8 30 6
tiri 1
```

## Livello 20 — Pista di ghiaccio (I)

**Intento:** tutto il tavolo è ghiaccio: le palle non rallentano, ogni errore continua a rimbalzare. **Soluzione:** la bianca (6,7) e la 8 (11,5) sono sulla stessa retta che va alla buca (16,3) (pendenza −2/5). Un tiro lungo quella linea, anche piano, manda la 8 in buca; la bianca si ferma (colpo centrale). **Errori:** colpo di taglio = la bianca scivola all'infinito e può cadere in buca (restart); la 8 sbagliata rimbalza finché non trova la buca (o resta in giro). Par 2.

```
#################
#IIIIIIIIIIIIIII#
#IIIIIIIIIIIIIII#
#IIIIIIIIIIIIIII#
#IIIIIIIIIIIIIII#
#IIIIIIIIIIIIIIIO
#IIIIIIIIIIIIIII#
#IIIIIIIIIIIIIII#
#################
---
0 18 21
8 33 15
tiri 2
```

## Livello 21 — Duna (M)

**Intento:** fascia di sabbia larga 8 tile: i tiri deboli si fermano. **Soluzione:** 1 tiro forte (almeno ~16 u/s, circa 63% di potenza; finestra fino al 100%): bianca (2,3) attraversa la sabbia, colpisce la 8 (9,3) e la 8 la attraversa fino alla buca (16,3). **Errori:** tiro debole: la bianca si ferma nella sabbia senza colpire nessuna palla = restart. Tiro medio: la 8 si pianta nella sabbia.

```
#################
#....MMMMMMMM...#
#....MMMMMMMM...#
#....MMMMMMMM...O
#....MMMMMMMM...#
#....MMMMMMMM...#
#################
---
0 6 9
8 27 9
tiri 1
```

## Livello 22 — Tombino (W)

**Intento:** corridoio di un tile con un tombino in mezzo: la 8 si ferma sempre al centro del tombino. **Soluzione:** tiro 1 manda la 8 (5,3) nel tombino (9,3) a qualunque potenza; tiro 2 la bianca (rimasta in z=3, a sinistra) la colpisce dritta e la manda in buca (14,3). Par 2 obbligato. **Errori:** tiro 1 troppo debole: la 8 non arriva al tombino (si ripete). La bianca non deve finire nel tombino.

```
###############
#......#####..#
#......#####..#
#........W....O
#......#####..#
#......#####..#
###############
---
0 6 9
8 15 9
tiri 2
```

Verifica: riga z=3 = `#` + 8 punti + `W` + 4 punti + `O` = 15; W all'indice 9.

## Livello 23 — Corrente (N)

**Intento:** tre colonne di nastro che spingono verso l'alto (+2.5 tile/s di spostamento) sul percorso. **Soluzione:** bianca (2,3) mira un poco sotto la 8 (12,3): circa 4° verso il basso con potenza media (10 u/s: spostamento ~0.75 tile). Più forte = meno deriva. Par 2 (primo tiro di calibrazione). **Errori:** mirare dritto: la bianca arriva 0.75 sopra e manca la 8 (nessuna palla colpita = restart); tiro troppo lento: deriva più di 1.5 tile e batte sulla sponda.

```
#################
#......NNN......#
#......NNN......#
#......NNN......O
#......NNN......#
#......NNN......#
#################
---
0 6 9
8 36 9
tiri 2
```

## Livello 24 — Cancello (K, G)

**Intento:** pulsante K (in alto a sinistra, 4,7) tiene aperti i cancelli G (9,2–9,5). Il nastro a due tile (4,5 e 4,6, verso l'alto) porta una palla ferma sul pulsante. **Soluzione:** tiro 1: bianca (4,1) dritta verso l'alto sulla palla 1 (4,3), che sale sul nastro e finisce sul pulsante (cancelli giù). Tiro 2: bianca (ferma circa (4,2.5)) verso destra in leggera salita attraverso il varco, colpisce la 8 (13,4) che va in buca (16,4). Il cancello resta aperto finché la palla 1 sta sul pulsante. **Errori:** tiro 1 troppo forte: la palla 1 rimbalza dalla sponda in alto e ritorna (il nastro la riporta su, di solito si ferma sul pulsante); troppo debole: si ferma prima del nastro (ripetere). Rete di sicurezza: palla 2 libera di scorta in (6,5). Se la palla 1 cade fuori dal tavolo il cancello non si apre più: ci sono sponde chiuse intorno al pulsante e alla sua corsia, la palla 2 è la scorta.

```
#################
#...K....#......#
#...N....#......#
#...N....G......#
#........G......O
#........G......#
#........G......#
#........#......#
#################
---
0 12 3
1 12 9
2 18 15
8 39 12
tiri 2
```

## Livello 25 — Doppio varco (L)

**Intento:** due colonne di muri a tempo (3 tile di larghezza, su 2 s / giù 2 s, partono su, stessa fase); la 8 sta tra le due colonne. **Soluzione:** aspettare che i muri siano abbassati (viola abbassato), tiro forte dritto: la bianca passa il primo varco (x=5), colpisce la 8 (7,3), che passa il secondo (x=9) ed entra in buca (14,3). Tutto in meno di 1 s dentro la finestra di 2 s. **Errori:** tiro appena prima della chiusura: la palla sbatte sul muro che sale (restart se la bianca non colpisce nulla); tiro debole: la 8 non fa in tempo a uscire dal secondo varco. Par 2.

```
###############
#....#...#....#
#....L...L....#
#....L...L....O
#....L...L....#
#....#...#....#
###############
---
0 6 9
8 21 9
tiri 2
```

## Livello 26 — Ponte mobile (H)

**Intento:** canyon di 4 tile (corridoio di un tile di larghezza, sponde ai lati); la bianca parte sulla piattaforma, che la porta avanti e indietro. **Soluzione:** aspettare che la piattaforma sia all'estremo destro (cella 8,3, circa 2 s dall'inizio, poi ogni 4 s), poi tiro dritto verso destra sulla 8 (11,3) che va in buca (14,3). **Errori:** tiro troppo presto: la bianca esce dalla piattaforma sul vuoto e cade = restart. Palle 1 e 2 sono solo scenografia a sinistra. Angolo della piattaforma: 90.

```
###############
#....####.....#
#....####.....#
#....H   .....O
#....####.....#
#....####.....#
###############
---
> 5 3 90
0 15 9
1 6 6
2 6 15
8 33 9
tiri 2
```

## Livello 27 — Calamita (Z)

**Intento:** la 8 parte agganciata al centro della calamita (8,3), tira entro 2.5 tile. **Soluzione:** un colpo forte e dritto dalla bianca (2,3) (distanza 6, fuori portata): la bianca accelera verso la calamita, colpisce la 8 (serve almeno ~9 u/s per uscire dal pozzo), la 8 corre fino alla buca (16,3). **Errori:** tiro debole: la 8 esce di poco e torna ad agganciarsi; la bianca nella portata viene agganciata anch'essa (ammassi di due palle: da verificare, vedi rischi). Par 2.

```
#################
#...............#
#...............#
#.......Z.......O
#...............#
#...............#
#################
---
0 6 9
8 24 9
tiri 2
```

## Livello 28 — Specchio di ghiaccio (T, Y, I)

**Intento:** portale ruotato di +90 che scarica la bianca su una lastra di ghiaccio, tiro in diagonale. **Soluzione:** la bianca (2,1) mira al blu (4,4), direzione (2,3); esce dall'arancione (7,6) con direzione ruotata (3,−2), arriva sulla 8 (10,4), che prosegue lungo (3,−2) e raggiunge la buca in basso (15,0). Geometria calcolata a mano (offset di ingresso incluso): la 8 viaggia da (10,4) a (15.25,0.5). Margine sulla bocca ±0.57. **Errori:** ingresso decentrato di 0.3 sposta l'uscita; ghiaccio = palle ovunque senza fermarsi (bianca in buca = restart). Par 2.

```
#################
#.....#IIIIIIIII#
#.....#YIIIIIIII#
#.....#IIIIIIIII#
#...T.#IIIIIIIII#
#.....#IIIIIIIII#
#.....#IIIIIIIII#
#.....#IIIIIIIII#
###############O#
---
> 4 4 0
> 7 6 90
0 6 3
8 30 12
tiri 2
```

## Livello 29 — Catena di montaggio (N, K, G, L)

**Intento:** meccanismo a due fasi. **Soluzione:** tiro 1: bianca (3,1) su palla 1 (3,3): sale sul nastro (3,5–3,6) e si ferma sul pulsante (3,7), cancelli (7,3–7,5) giù. Tiro 2: aspettare che il muro a tempo (colonna x=11, tutta l'altezza) sia abbassato, poi tiro forte dalla bianca (ferma circa (3,2.5)) verso (13,4): passa il cancello (z≈3.1), il muro, colpisce la 8 che va in buca (16,4). **Errori:** tiro 2 con muro in salita; la palla 1 che cade; la 8 colpita di taglio (cut ~8°, tollerato). Palla 2 di scorta in (5,5). Par 3 (un tentativo di scorta per il tempismo del muro).

```
#################
#..K...#...L....#
#..N...#...L....#
#..N...G...L....#
#......G...L....O
#......G...L....#
#......#...L....#
#......#...L....#
#################
---
0 9 3
1 9 9
2 15 15
8 39 12
tiri 3
```

## Livello 30 — Gran finale (H, Z, Q)

**Intento:** piattaforma + calamita + respingenti. **Soluzione:** la bianca parte sulla piattaforma (5,3), canyon di 4 tile (celle 6–9, range 4: ~2.7 s per tratta). Attendere l'estremo destro (cella 9,3), poi tiro verso la 8 (13,4) che è agganciata alla calamita: direzione (4,1). La 8 esce lungo (4,1) (radiale, quindi dritta) e passa tra i due respingenti (16,4) e (16,6), varco centrato a z=5 con finestra 4.55–5.45, entra nella buca (18,5). **Errori:** tiro anticipato (caduta nel canyon); tiro debole (la 8 resta nel pozzo); errore laterale sul varco (il respingente calcia via la 8). Palle 1 e 2 sono scenografia. Par 3.

```
###################
#....#####........#
#....#####......Q.#
#....#####........O
#....#####...Z..Q.#
#....H    ........#
#....#####........#
#....#####........#
###################
---
> 5 3 90
0 15 9
1 6 6
2 6 15
8 39 12
tiri 3
```

---

## 4. Rischi (da testare per primi, in ordine)

1. **30, 27, 29, 24 (palla ferma sulla calamita / sul pulsante):** il comportamento di calamita con 2 palle sovrapposte e con bianca nella portata (27, 30) non è verificato nel codice (spinta fuori di `BallBall`, gittata del pozzo ~9 u/s calcolata a mano). Controllare anche la possibilità di softlock (palla 1 persa nel 24/29); presumo che il menu pausa permetta di riavviare.
2. **28 e 19 (portali):** offset di ingresso/uscita e rotazione (`Teleport`); la diagonale del 28 è una soluzione calcolata a mano. Verificare in gioco che `O` con panno-ghiaccio davanti (28, 20) dia comunque una buca laterale (non letto in `LevelBuilder.Table`).
3. **26 e 30 (piattaforma):** bianca ferma sulla piattaforma portata (`Modules` gira anche per palle ferme, ok), ma verificare che `GameManager` non consideri la bianca "in movimento" a ogni frame e che il tiro sia permesso mentre viaggia; verificare l'attrito su tile `H` (`Cloth` usa `TileAt`).
4. **23 (nastro):** deriva calcolata a mano (≈4°), da tarare con una prova; la linea bianca singolo giocatore potrebbe non includere il nastro.
5. **16/17 (respingenti):** livelli quasi banali; il tunnel 17 dipende dalla velocità che aumenta a ogni urto (fino a ~30): controllare che la 8 non esca di sponda. Da ripensare dopo la prova se troppo facili (par 1).
6. **25, 29 (muri a tempo):** fase partita al caricamento e muro che sale su una palla in transito (palla in un muro che risale: da vedere); finestra di 2 s sufficiente.
7. **20 (ghiaccio totale):** bianca e 8 senza attrito; con tiro piano la bianca potrebbe scivolare per sempre (restart per buca).
8. **Tema Laguna:** da verificare con screenshot (piattaforma verde su panno verde acqua; nastro scuro). Il testo del tema sopra contiene i valori da mettere in `Worlds.Themes[1]` (non modificato, fuori dal mio perimetro).
