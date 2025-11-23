using NUnit.Framework;
using UnityEngine;

public class SimpleGroundCheckerTests
{
    [Test]
    public void IsOverGroundObject_ReturnsFalse_IfLayerMaskIsZero()
    {
        LayerMask mask = 0;
        var checker = new SimpleGroundChecker(mask);
        Vector3 pos = Vector3.zero;
        bool result = checker.IsOverGroundObject(pos);
        Assert.IsFalse(result);
    }
}
