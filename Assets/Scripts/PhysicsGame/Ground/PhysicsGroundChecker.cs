using UnityEngine;

public class PhysicsGroundChecker : MonoBehaviour, IGroundChecker
    {
    [SerializeField] private LayerMask groundLayerMask = 0;

    public LayerMask GroundLayerMask => groundLayerMask;

    private void Reset()
    {
        if (groundLayerMask == 0)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
                groundLayerMask = 1 << groundLayer;
        }
    }

    public bool IsOverGroundObject(Vector3 worldPosition)
    {
        if (groundLayerMask == 0) return false;
        Collider2D c = Physics2D.OverlapPoint(new Vector2(worldPosition.x, worldPosition.y), groundLayerMask.value);
        return c != null;
    }
    }
