using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One world on the world select (MenuView.worldCard is the template, cloned once per world at runtime).
/// GameUI colors frame / felt with the world's theme and fills the texts; any of them may be left empty.
/// </summary>
public class MenuWorldCard : MonoBehaviour
{
    public Button button;
    [Tooltip("Prende il colore del legno del mondo")] public Image frame;
    [Tooltip("Prende il colore del panno del mondo")] public Image felt;
    [Tooltip("Numero del mondo")] public Text number;
    public Text title;
    [Tooltip("Livelli finiti / totale, corone; \"BLOCCATO\" se il mondo è chiuso")] public Text progress;
    [Tooltip("Trasparenza della carta di un mondo bloccato")] [Range(0, 1)] public float lockedAlpha = 0.45f;
    [Tooltip("Anteprima 3D del primo livello del mondo, che gira")] public RawImage preview;
    [Tooltip("Pallino modello: si copia una volta per livello del mondo nello stesso genitore")] public Image dot;
    public Color dotTodo = new(0.96f, 0.92f, 0.84f, 0.25f), dotDone = new(0.96f, 0.92f, 0.84f), dotCrown = new(1f, 0.78f, 0.15f);
    [Tooltip("Acceso solo sulla carta scelta (es. un Outline dorato)")] public Behaviour highlight;
}
