using UnityEngine;

public class SimpleGroundChecker : IGroundChecker
    {
    private readonly LayerMask layerMask;

    public SimpleGroundChecker(LayerMask layerMask)
    {
        this.layerMask = layerMask;
    }

    public bool IsOverGroundObject(Vector3 worldPosition)
    {
        if (layerMask == 0) return false;
        Collider2D c = Physics2D.OverlapPoint(new Vector2(worldPosition.x, worldPosition.y), layerMask.value);
        return c != null;
    }
    }
