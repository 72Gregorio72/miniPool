using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// "Crea livelli" screens: the user's levels, the editor HUD (Geometry Dash style: tabs on the left of a bottom panel,
// palette in the middle, floor / rotation on the right, undo / playtest / save at the top), its cards and the results
// of a user level. The editing is LevelEditor's; every button calls it and it rebuilds this HUD.
public partial class GameUI
{
    const int LevelsPerPage = 6;
    static readonly Color Warn = new(1f, 0.72f, 0.3f), Panel = new(0.08f, 0.09f, 0.11f, 0.9f), Tile = new(1, 1, 1, 0.08f);
    Action myLevelsBack;
    int myLevelsPage;

    // ---------- the user's levels ----------

    public void ShowMyLevels(List<string> names, Action create, Action<string> edit, Action<string> play, Action<string> copy, Action<string> duplicate, Action<string> export,
        Action<string> delete, Action paste, Action import, Action folder, Action back)
    {
        Hide();
        screen = Page.MyLevels;
        myLevelsBack = back;
        var full = Rect(root, Vector2.zero, Vector2.zero);
        Box(full, new Color(0, 0, 0, 0.5f), Vector2.zero, Vector2.zero, null);
        var top = Corner(full, new Vector2(0, 1));
        Label(top, "M I N I P O O L", 26, new Color(1, 1, 1, 0.7f), new Vector2(90, -70), new Vector2(600, 40), TextAnchor.MiddleLeft, true, true);
        Label(top, "Crea livelli", 76, White, new Vector2(90, -135), new Vector2(1000, 90), TextAnchor.MiddleLeft, true, true);
        Btn(Corner(full, new Vector2(1, 1)), "Indietro", Glass, White, new Vector2(-170, -80), new Vector2(220, 70), back, round);

        var c = Corner(full, new Vector2(0.5f, 0.5f));
        var accent = Worlds.ThemeOf(0).accent;
        Bar(c, new Vector2(0, 300), new Vector2(320, 84), 20, ("Nuovo livello", accent, Ink, create), ("Incolla livello", Glass, White, paste),
            ("Importa file", Glass, White, import), ("Apri cartella", Glass, White, folder));

        int pages = Mathf.Max(1, (names.Count + LevelsPerPage - 1) / LevelsPerPage);
        myLevelsPage = Mathf.Clamp(myLevelsPage, 0, pages - 1);
        if (names.Count == 0)
            Label(c, "Ancora nessun livello: creane uno con «Nuovo livello» o importa un file", 32, new Color(1, 1, 1, 0.75f), new Vector2(0, 60), new Vector2(1400, 50), shadow: true);
        var shown = names.Skip(myLevelsPage * LevelsPerPage).Take(LevelsPerPage).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            string n = shown[i];
            float y = 180 - i * 96;
            var row = Box(c, Tile, new Vector2(0, y), new Vector2(1500, 84));
            Label(row, n, 34, White, new Vector2(-720, 0), new Vector2(500, 84), TextAnchor.MiddleLeft, true);
            Button del = null;
            bool armed = false;
            del = Bar(row, new Vector2(265, 0), new Vector2(150, 64), 10, ("Modifica", accent, Ink, () => edit(n)), ("Gioca", Glass, White, () => play(n)),
                ("Copia", Glass, White, () => copy(n)), ("Duplica", Glass, White, () => duplicate(n)), ("Esporta", Glass, White, () => export(n)), ("Elimina", Glass, White, () =>
                {
                    if (armed) { delete(n); return; }
                    armed = true; // second click deletes
                    del.GetComponentInChildren<Text>().text = "Sicuro?";
                }))[5];
            del.GetComponentInChildren<Text>().fontSize = 22;
        }
        if (pages > 1)
        {
            float by = 180 - LevelsPerPage * 96 + 10;
            void Turn(int d) { myLevelsPage += d; ShowMyLevels(names, create, edit, play, copy, duplicate, export, delete, paste, import, folder, back); }
            if (myLevelsPage > 0) ArrowButton(c, "‹", new Vector2(-120, by), () => Turn(-1));
            Label(c, $"{myLevelsPage + 1} / {pages}", 30, White, new Vector2(0, by), new Vector2(200, 50), bold: true, shadow: true);
            if (myLevelsPage < pages - 1) ArrowButton(c, "›", new Vector2(120, by), () => Turn(1));
        }
        Label(Corner(full, new Vector2(0.5f, 0)), "Nei tuoi livelli l'obiettivo è mandare in buca la 8.   Cartella: " + LevelEditor.Dir, 22,
            new Color(1, 1, 1, 0.6f), new Vector2(0, 40), new Vector2(1800, 36), shadow: true);
    }

    // ---------- editor HUD ----------

    public void ShowEditor(LevelEditor e)
    {
        Hide();
        screen = Page.Editor;
        var full = Rect(root, Vector2.zero, Vector2.zero);
        var accent = Worlds.ThemeOf(0).accent;

        // top bar: undo / redo + name on the left, playtest / save / copy / paste / export / settings / exit on the right
        var tl = Corner(full, new Vector2(0, 1));
        var undo = SmallBtn(tl, "↶ Annulla", new Vector2(110, -55), new Vector2(180, 64), e.Undo);
        var redo = SmallBtn(tl, "↷ Ripeti", new Vector2(300, -55), new Vector2(180, 64), e.Redo);
        undo.interactable = e.CanUndo;
        redo.interactable = e.CanRedo;
        Label(tl, e.editName + (e.dirty ? " *" : ""), 34, White, new Vector2(410, -55), new Vector2(700, 64), TextAnchor.MiddleLeft, true, true);
        var warnings = e.Warnings.Take(3).ToList();
        for (int i = 0; i < warnings.Count; i++)
            Label(tl, "⚠ " + warnings[i], 24, Warn, new Vector2(30, -115 - i * 34), new Vector2(1100, 34), TextAnchor.MiddleLeft, true, true);
        Label(tl, LevelDraft.HelpOf(e.brush), 22, new Color(1, 1, 1, 0.8f), new Vector2(30, -115 - warnings.Count * 34), new Vector2(1300, 34), TextAnchor.MiddleLeft, false, true);

        var tr = Corner(full, new Vector2(1, 1));
        foreach (var b in Bar(tr, new Vector2(-568, -55), new Vector2(148, 64), 10, ("▶ Prova", accent, Ink, e.Playtest), ("Salva", Glass, White, () => e.Save()),
            ("Copia", Glass, White, e.CopyLevel), ("Incolla", Glass, White, e.PasteLevel), ("Esporta", Glass, White, e.ExportCurrent),
            ("Livello…", Glass, White, e.Settings), ("Esci", Glass, White, e.Exit)))
            b.GetComponentInChildren<Text>().fontSize = 24;

        // bottom panel
        var strip = Rect(full, Vector2.zero, Vector2.one);
        strip.anchorMin = Vector2.zero;
        strip.anchorMax = new Vector2(1, 0);
        strip.pivot = new Vector2(0.5f, 0);
        strip.sizeDelta = new Vector2(0, 300);
        strip.anchoredPosition = Vector2.zero;
        var bg = strip.gameObject.AddComponent<Image>();
        bg.color = Panel;
        bg.raycastTarget = true; // clicks on the panel don't paint the table under it
        var bc = Corner(full, new Vector2(0.5f, 0));
        Label(bc, "Sinistro disegna · Shift+trascina rettangolo · Ctrl+clic cancella · P pipetta · Destro trascina sposta la vista · Rotella zoom · Rotella premuta o Alt+sinistro gira · " +
            "R/Q ruota (anche il pezzo in mano) · PagSu/PagGiù piano · T dall'alto · Home inquadra · 1/2/3 schede · Ctrl+Z/Y", 20, new Color(1, 1, 1, 0.75f), new Vector2(0, 322), new Vector2(1880, 30), shadow: true);

        var tabs = new[] { ("Costruisci", LevelEditor.Tab.Build), ("Modifica", LevelEditor.Tab.Edit), ("Elimina", LevelEditor.Tab.Delete) };
        for (int i = 0; i < tabs.Length; i++)
        {
            var (s, t) = tabs[i];
            bool on = e.tab == t;
            Btn(bc, s, on ? accent : Glass, on ? Ink : White, new Vector2(-835, 240 - i * 90), new Vector2(200, 76), () => e.SelectTab(t), round).GetComponentInChildren<Text>().fontSize = 26;
        }

        if (e.tab == LevelEditor.Tab.Build) BuildPalette(bc, e, accent);
        else if (e.tab == LevelEditor.Tab.Edit) EditTools(bc, e, accent);
        else DeleteTools(bc, e, accent);

        // right column: floor, rotation, stacking, column
        Stepper(bc, "Piano", e.floor.ToString(), new Vector2(760, 240), d => e.ChangeFloor(d));
        bool oriented = LevelBuilder.IsOriented(e.brush);
        Stepper(bc, "Rotazione", oriented && e.brush == LevelBuilder.Track && e.tilt != 0 ? (e.tilt > 0 ? "su" : "giù") : oriented ? $"{e.SnappedAngle}°" : e.TurningClip ? "pezzo" : "—", new Vector2(760, 150), d => e.Rotate(d * (e.TurningClip ? 90 : LevelDraft.RotateStep)));
        SmallBtn(bc, e.stack ? "Sovrapposizione: sì" : "Sovrapposizione: no", new Vector2(760, 82), new Vector2(300, 44), e.ToggleStack)
            .GetComponent<Image>().color = e.stack ? accent : Glass;
        SmallBtn(bc, e.column ? "Colonna piena: sì" : "Colonna piena: no", new Vector2(760, 32), new Vector2(300, 44), e.ToggleColumn)
            .GetComponent<Image>().color = e.column ? accent : Glass;
    }

    void BuildPalette(Transform bc, LevelEditor e, Color accent)
    {
        var cats = LevelDraft.Categories;
        for (int i = 0; i < cats.Length; i++)
        {
            int k = i;
            bool on = e.category == i;
            var b = SmallBtn(bc, cats[i], new Vector2(-610 + i * 190, 262), new Vector2(180, 48), () => e.SelectCategory(k));
            b.GetComponent<Image>().color = on ? accent : Glass;
            b.GetComponentInChildren<Text>().color = on ? Ink : White;
        }
        var items = e.category == 1
            ? new[] { ("Bianca", '0') }.Concat(LevelDraft.BallChars.Select((c, i) => (i + 1 == LevelEditor.Goal ? "8 ★" : $"{i + 1}", c)))
                .Concat(LevelDraft.Brushes.Where(b => b.category == 1 && b.c != '0').Select(b => (b.label, b.c))).ToList()
            : LevelDraft.Brushes.Where(b => b.category == e.category).Select(b => (b.label, b.c)).ToList();
        for (int i = 0; i < items.Count; i++)
        {
            var (label, c) = items[i];
            bool on = e.brush == c;
            var pos = new Vector2(-640 + (i % 9) * 140, 168 - (i / 9) * 116);
            var b = Btn(bc, "", on ? accent : Tile, Ink, pos, new Vector2(130, 108), () => e.SetBrush(c), round);
            var icon = LevelEditor.Icon(c);
            if (icon)
            {
                var img = Rect(b.transform, new Vector2(0, 12), new Vector2(76, 76)).gameObject.AddComponent<RawImage>();
                img.texture = icon;
                img.raycastTarget = false;
            }
            Label(b.transform, label.Replace(" (R)", ""), 17, on ? Ink : White, new Vector2(0, -40), new Vector2(126, 24), bold: true);
        }
    }

    void EditTools(Transform bc, LevelEditor e, Color accent)
    {
        void Tool(string s, char c, float x)
        {
            bool on = e.brush == c;
            var b = Btn(bc, s, on ? accent : Glass, on ? Ink : White, new Vector2(x, 225), new Vector2(260, 76), () => e.SetBrush(c), round);
            b.GetComponentInChildren<Text>().fontSize = 26;
        }
        Tool("Sposta", LevelDraft.MoveTool, -590);
        Tool("Copia / Incolla", LevelDraft.CopyTool, -320);
        if (e.d.clip != null) SmallBtn(bc, "Svuota (Esc)", new Vector2(-80, 225), new Vector2(200, 76), e.CancelClip);
        var clip = e.d.clip;
        string status = clip == null ? "Niente in mano"
            : clip.fine ? "In mano: una palla"
            : $"{(e.d.Moving ? "In mano" : "Appunti")}: {clip.max.x - clip.min.x + 1}×{clip.max.y - clip.min.y + 1} tile";
        Label(bc, status, 24, White, new Vector2(110, 225), new Vector2(440, 40), TextAnchor.MiddleLeft, true);
        Label(bc, e.brush == LevelDraft.CopyTool
                ? "Copia: trascina per copiare un'area (tile, piani, palle), poi clic per incollarla, anche più volte o in un altro livello. Shift + trascina = nuova copia."
                : "Sposta: clic su un ostacolo o una palla per prenderlo (trascina = tutti gli ostacoli di un'area), poi clic dove lasciarlo. Panno, sponde e buche restano.",
            22, new Color(1, 1, 1, 0.75f), new Vector2(-720, 120), new Vector2(1260, 90), TextAnchor.MiddleLeft)
            .horizontalOverflow = HorizontalWrapMode.Wrap; // stays inside the panel
        SmallBtn(bc, "↺ 90°", new Vector2(-610, 45), new Vector2(160, 64), () => e.Rotate(-90));
        SmallBtn(bc, "↻ 90°", new Vector2(-440, 45), new Vector2(160, 64), () => e.Rotate(90));
        Label(bc, e.TurningClip ? "Ruota il pezzo in mano (R / Q)" : "Ruota il pennello (spinte, rampe, portali, nastri, ventilatori…)", 22, new Color(1, 1, 1, 0.75f), new Vector2(-340, 45), new Vector2(900, 40), TextAnchor.MiddleLeft);
    }

    void DeleteTools(Transform bc, LevelEditor e, Color accent)
    {
        var b = Btn(bc, "Gomma", e.brush == ' ' ? accent : Glass, e.brush == ' ' ? Ink : White, new Vector2(-590, 225), new Vector2(260, 76), () => e.SetBrush(' '), round);
        b.GetComponentInChildren<Text>().fontSize = 26;
        SmallBtn(bc, "Svuota livello", new Vector2(-320, 225), new Vector2(260, 76), e.Clear);
        Label(bc, "Clic o trascina per cancellare tile, moduli e palle (Shift + trascina = rettangolo). Ctrl + clic cancella con qualunque pennello.\n" +
            "Con «Sovrapposizione: sì» la gomma toglie solo il modulo in cima alla pila.", 22, new Color(1, 1, 1, 0.75f), new Vector2(-720, 110), new Vector2(1260, 90), TextAnchor.MiddleLeft)
            .horizontalOverflow = HorizontalWrapMode.Wrap;
    }

    // "name  ‹ value ›" in the right column
    void Stepper(Transform parent, string name, string value, Vector2 pos, Action<int> change)
    {
        Label(parent, name, 24, new Color(1, 1, 1, 0.75f), pos + new Vector2(-150, 0), new Vector2(140, 56), TextAnchor.MiddleLeft, true);
        SmallBtn(parent, "‹", pos + new Vector2(20, 0), new Vector2(56, 56), () => change(-1)).GetComponentInChildren<Text>().fontSize = 40;
        Label(parent, value, 28, White, pos + new Vector2(90, 0), new Vector2(84, 56), bold: true);
        SmallBtn(parent, "›", pos + new Vector2(160, 0), new Vector2(56, 56), () => change(1)).GetComponentInChildren<Text>().fontSize = 40;
    }

    Button SmallBtn(Transform parent, string s, Vector2 pos, Vector2 size, Action click)
    {
        var b = Btn(parent, s, Glass, White, pos, size, click, round);
        b.GetComponentInChildren<Text>().fontSize = 22;
        return b;
    }

    // ---------- editor cards ----------

    public void ShowEditorSettings(LevelEditor e)
    {
        Hide();
        screen = Page.Editor;
        var dimmer = Box(root, new Color(0, 0, 0, 0.5f), Vector2.zero, Vector2.zero, null);
        dimmer.GetComponent<Image>().raycastTarget = true;
        const float w = 820, h = 660;
        var card = Box(dimmer, White, Vector2.zero, new Vector2(w, h));
        pop = card;
        popT = 0;
        Label(card, "Impostazioni del livello", 48, Ink, new Vector2(0, h / 2 - 70), new Vector2(w, 70), bold: true);
        Label(card, "Nome", 28, Grey, new Vector2(-w / 2 + 60, 170), new Vector2(200, 50), TextAnchor.MiddleLeft, true);
        var field = Input(card, "Nome del livello", new Vector2(90, 170), new Vector2(520, 80), name: true);
        field.text = e.editName;
        field.onEndEdit.AddListener(e.Rename);
        Background(field);

        void Again(Action change)
        {
            e.Rename(field.text);
            change();
            ShowEditorSettings(e);
        }
        CardRow(card, 60, "Tiri minimi (corona)", e.d.par > 0 ? e.d.par.ToString() : "nessuna", dlt => Again(() => e.SetPar(e.d.par + dlt)));
        CardRow(card, -40, "Falli concessi", e.d.fouls.ToString(), dlt => Again(() => e.SetFouls(e.d.fouls + dlt)));
        int theme = Mathf.Max(0, e.d.theme);
        CardRow(card, -140, "Colori (mondo)", Worlds.ThemeOf(theme).name, dlt => Again(() => e.SetTheme(theme + dlt)));
        Label(card, "Obiettivo: mandare in buca la 8.  Corona = finirlo in al massimo «tiri minimi» tiri.", 22, Grey, new Vector2(0, -215), new Vector2(w - 60, 34));
        Btn(card, "Chiudi", Worlds.ThemeOf(0).accent, Ink, new Vector2(0, -h / 2 + 70), new Vector2(260, 80), () => { e.Rename(field.text); e.ShowHud(); }, round);
    }

    // the name field on the white card: a soft box behind it, or it vanishes
    static void Background(InputField f) => f.GetComponent<Image>().color = Soft;

    void CardRow(Transform card, float y, string name, string value, Action<int> change)
    {
        Label(card, name, 28, Grey, new Vector2(-350, y), new Vector2(360, 60), TextAnchor.MiddleLeft, true);
        var minus = Btn(card, "‹", Soft, Ink, new Vector2(90, y), new Vector2(70, 70), () => change(-1), circle);
        minus.GetComponentInChildren<Text>().fontSize = 44;
        Label(card, value, 30, Ink, new Vector2(215, y), new Vector2(180, 60), bold: true);
        var plus = Btn(card, "›", Soft, Ink, new Vector2(340, y), new Vector2(70, 70), () => change(1), circle);
        plus.GetComponentInChildren<Text>().fontSize = 44;
    }

    public void ShowEditorExit(Action saveAndExit, Action exit, Action cancel)
    {
        Hide();
        screen = Page.Editor;
        var dimmer = Box(root, new Color(0, 0, 0, 0.5f), Vector2.zero, Vector2.zero, null);
        dimmer.GetComponent<Image>().raycastTarget = true;
        const float w = 640, h = 520;
        var card = Box(dimmer, White, Vector2.zero, new Vector2(w, h));
        pop = card;
        popT = 0;
        Label(card, "Modifiche non salvate", 48, Ink, new Vector2(0, h / 2 - 80), new Vector2(w, 70), bold: true);
        Btn(card, "Salva ed esci", Worlds.ThemeOf(0).accent, Ink, new Vector2(0, 60), new Vector2(420, 84), saveAndExit, round);
        Btn(card, "Esci senza salvare", Soft, Ink, new Vector2(0, -45), new Vector2(420, 84), exit, round);
        Btn(card, "Annulla", Soft, Ink, new Vector2(0, -150), new Vector2(420, 84), cancel, round);
    }

    // ---------- user level results ----------

    public void ShowCustomResults(string name, int shots, int par, int attempts, float seconds, Action again, Action back)
    {
        Hide();
        var dimmer = Box(root, new Color(0, 0, 0, 0.45f), Vector2.zero, Vector2.zero, null);
        const float w = 760, h = 500;
        var card = Box(dimmer, White, new Vector2(0, -20), new Vector2(w, h));
        pop = card;
        popT = 0;
        var ball = Rect(card, new Vector2(0, h / 2), Vector2.one * 150);
        AddModel(ball, BallArt.Material(LevelEditor.Goal)).spin = true;
        if (Rules.Crown(shots, par)) CrownOutline(ball);
        Label(card, "Livello completato!", 52, Ink, new Vector2(0, h / 2 - 125), new Vector2(w, 70), bold: true);
        Label(card, name, 28, Grey, new Vector2(0, h / 2 - 175), new Vector2(w, 40));
        var stats = new[] { (par > 0 ? $"Tiri (corona: {par})" : "Tiri", shots.ToString()), ("Tentativi", attempts.ToString()), ("Tempo", $"{(int)seconds / 60}:{(int)seconds % 60:00}") };
        for (int i = 0; i < stats.Length; i++)
        {
            var x = (i - 1) * 220f;
            Box(card, Soft, new Vector2(x, -20), new Vector2(200, 140));
            Label(card, stats[i].Item2, 60, Ink, new Vector2(x, 0), new Vector2(200, 80), bold: true);
            Label(card, stats[i].Item1, 24, Grey, new Vector2(x, -60), new Vector2(200, 36));
        }
        Btn(card, "Esci", Soft, Ink, new Vector2(-140, -h / 2 + 70), new Vector2(240, 76), back, round);
        Btn(card, "Rigioca", Worlds.ThemeOf(0).accent, Ink, new Vector2(140, -h / 2 + 70), new Vector2(240, 76), again, round);
    }
}
