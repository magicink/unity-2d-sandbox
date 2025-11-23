using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Tests for DragController logic that doesn't require a full Unity scene.
public class DragControllerTests
{
    private class FakePlayerInteractor : IPlayerInteractor
    {
        public List<Vector3> BeginCalls = new List<Vector3>();
        public List<Vector3> UpdateCalls = new List<Vector3>();
        public List<Vector3?> EndCalls = new List<Vector3?>();
        public bool Respawned = false;
        public Camera LastRespawnCam = null;
        public float LastRespawnZ = 0f;

        public bool IsAirborne => false;
        public bool IsGrounded => false;

        public void BeginDragAt(Vector3 world) { BeginCalls.Add(world); }
        public void UpdateDragTarget(Vector3 world) { UpdateCalls.Add(world); }
        public void EndDragAt(Vector3? launchVelocity) { EndCalls.Add(launchVelocity); }
        public void RespawnToWorldOrigin(Camera cam, float zDepth) { Respawned = true; LastRespawnCam = cam; LastRespawnZ = zDepth; }
    }

    private class FakeGroundChecker : IGroundChecker
    {
        public bool ShouldBeOverGround = false;
        public bool IsOverGroundObject(Vector3 worldPosition) => ShouldBeOverGround;
    }

    private Vector3 ScreenToWorldSimple(Vector2 screen)
    {
        // trivial mapping so we can reason about distances
        return new Vector3(screen.x * 0.01f, screen.y * 0.01f, 0f);
    }

    [Test]
    public void Begin_WhenStartOverGround_InvokesBlockAndDoesNotStartFollow()
    {
        var player = new FakePlayerInteractor();
        var ground = new FakeGroundChecker { ShouldBeOverGround = true };
        var controller = new DragController(player, ground, ScreenToWorldSimple, preventStartOverGround: true);

        bool blocked = false;
        controller.DragStartBlocked += (pos) => blocked = true;

        controller.Begin(new Vector2(100f, 100f), 1);

        Assert.IsFalse(controller.IsFollowing);
        Assert.IsTrue(blocked);
        Assert.IsEmpty(player.BeginCalls);
    }

    [Test]
    public void Begin_WhenNotOverGround_StartsFollowAndCallsPlayerBegin()
    {
        var player = new FakePlayerInteractor();
        var ground = new FakeGroundChecker { ShouldBeOverGround = false };
        var controller = new DragController(player, ground, ScreenToWorldSimple, preventStartOverGround: true);

        bool started = false;
        controller.DragStarted += (pos) => started = true;

        controller.Begin(new Vector2(50f, 20f), 2);

        Assert.IsTrue(controller.IsFollowing);
        Assert.IsTrue(started);
        Assert.AreEqual(1, player.BeginCalls.Count);
        Assert.AreEqual(ScreenToWorldSimple(new Vector2(50f, 20f)), player.BeginCalls[0]);
        Assert.AreEqual(controller.GizmoStartWorld, player.BeginCalls[0]);
    }

    [Test]
    public void Continue_UpdatesPlayerTarget_AndEmitsDragUpdated()
    {
        var player = new FakePlayerInteractor();
        var ground = new FakeGroundChecker { ShouldBeOverGround = false };
        var controller = new DragController(player, ground, ScreenToWorldSimple, simulateTension: true, tensionStiffness: 1f, maxPullDistance: 5f);

        bool updated = false;
        controller.DragUpdated += (pos) => updated = true;

        controller.Begin(new Vector2(10f, 0f), 0);
        controller.Continue(new Vector2(40f, 0f));

        Assert.IsTrue(updated);
        Assert.IsTrue(player.UpdateCalls.Count > 0);
        Assert.AreEqual(controller.LastFollowWorldPosition, player.UpdateCalls[player.UpdateCalls.Count - 1]);
    }

    [Test]
    public void End_WhenReleaseOverGround_RespawnsAndEndsWithZeroVelocity()
    {
        var player = new FakePlayerInteractor();
        var ground = new FakeGroundChecker { ShouldBeOverGround = false };
        var controller = new DragController(player, ground, ScreenToWorldSimple);

        // Begin follow, then simulate release where ground checker returns true for release position
        controller.Begin(new Vector2(0f, 0f), 0);
        // set release over ground
        ground.ShouldBeOverGround = true;

        controller.End();

        Assert.IsTrue(player.Respawned);
        Assert.IsTrue(player.EndCalls.Count > 0);
        Assert.AreEqual(Vector3.zero, player.EndCalls[player.EndCalls.Count - 1]);
        Assert.IsFalse(controller.IsFollowing);
    }

    [Test]
    public void End_WhenSlingshotIsEnabled_ComputesLaunchVelocityAndCallEnd()
    {
        var player = new FakePlayerInteractor();
        var ground = new FakeGroundChecker { ShouldBeOverGround = false };
        var controller = new DragController(player, ground, ScreenToWorldSimple, simulateTension: false, enableSlingshot: true, slingshotForceMultiplier: 10f, minSlingshotDistance: 0.01f);

        // Begin at a point, then continue to create a pull and end
        controller.Begin(new Vector2(0f, 0f), 0);
        controller.Continue(new Vector2(100f, 0f)); // 1 unit away due to ScreenToWorldSimple mapping

        controller.End();

        Assert.IsFalse(controller.IsFollowing);
        Assert.IsTrue(player.EndCalls.Count > 0);
        var last = player.EndCalls[player.EndCalls.Count - 1];
        Assert.IsTrue(last.HasValue && last.Value.magnitude > 0f);
    }
}
