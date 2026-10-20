using UnityEngine;

public class BreakableBarrier : MonoBehaviour, IBreakable
{
    private Collider2D coll;
    private float lifeTime = 0.5f;
    private void Awake()
    {
        coll = GetComponent<Collider2D>();
    }
    public void Break()
    {
        coll.enabled = false;
        Destroy(gameObject, lifeTime);
        //SoundManager.instance.RandomPlaySound();
    }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.TryGetComponent<PlayerController2D>(out var player))
        {
            if (player.State == PlayerController2D.MoveState.Dashing)
            {
                if (CameraEffects.Instance != null)
                {
                    CameraEffects.Instance.HeavyShake();
                    CameraEffects.Instance.FreezeFrame(0.14f);
                }
                Break();
                player.RefillDash();

                if (collision.gameObject.TryGetComponent<PlayerStress>(out var ps))
                    ps.ApplyBreakingBarrierStress();
            }
        }
    }
}

/// <summary>Backward compatibility alias for Greybox Level Generator.</summary>
public class BreakingBarrier : BreakableBarrier {}
