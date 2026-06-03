using UnityEngine;
using UnityEngine.UI;

public class SpinUI : MonoBehaviour
{
    public ThrowBall throwBall;
    
    private float currentX = 0f;
    private float currentY = 0f;

    public void OnTopClick() => UpdateSpin(0, 5f);
    public void OnBottomClick() => UpdateSpin(0, -5f);
    public void OnLeftClick() => UpdateSpin(-5f, 0);
    public void OnRightClick() => UpdateSpin(5f, 0);
    public void OnResetClick() => UpdateSpin(0, 0);

    private void UpdateSpin(float x, float y)
    {
        currentX = x;
        currentY = y;
        if (throwBall != null)
        {
            throwBall.SetSpin(currentX, currentY);
        }
        Debug.Log($"Spin aggiornato: X={currentX}, Y={currentY}");
    }
}
