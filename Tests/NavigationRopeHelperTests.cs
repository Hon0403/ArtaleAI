using System.Collections.Generic;
using System.Drawing;
using ArtaleAI.Domain.Navigation;
using Xunit;

namespace ArtaleAI.Tests;

public sealed class NavigationRopeHelperTests
{
    private static readonly IReadOnlyList<(float X, float TopY, float BottomY)> Rope =
        new List<(float X, float TopY, float BottomY)> { (38.6f, 105.9f, 122.8f) };

    [Fact]
    public void IsPositionOnRope_MidSegment_True()
    {
        Assert.True(NavigationRopeHelper.IsPositionOnRope(
            new PointF(38.1f, 113.6f), Rope, ropeXTolerancePx: 1.5f, endpointYTolerancePx: 1.5f));
    }

    [Fact]
    public void IsPositionOnRope_NearEndpoint_False()
    {
        Assert.False(NavigationRopeHelper.IsPositionOnRope(
            new PointF(38.6f, 106.0f), Rope, ropeXTolerancePx: 1.5f, endpointYTolerancePx: 1.5f));
    }

    [Fact]
    public void ResolveClimbDirection_GoalAbove_ClimbUp()
    {
        Assert.Equal(
            NavigationActionType.ClimbUp,
            NavigationRopeHelper.ResolveClimbDirection(playerY: 113.6f, goalY: 106.0f));
    }

    [Fact]
    public void ResolveClimbDirection_GoalBelow_ClimbDown()
    {
        Assert.Equal(
            NavigationActionType.ClimbDown,
            NavigationRopeHelper.ResolveClimbDirection(playerY: 113.6f, goalY: 123.0f));
    }

    [Fact]
    public void TryPickClimbTowardGoal_MidRopeGoalAbove_PicksClimbUp()
    {
        var climbUp = new NavigationEdge("bot", "top", NavigationActionType.ClimbUp)
        {
            InputSequence = new List<string> { "ropeX:38.6" }
        };
        var climbDown = new NavigationEdge("top", "bot", NavigationActionType.ClimbDown)
        {
            InputSequence = new List<string> { "ropeX:38.6" }
        };

        var query = new RopeClimbPickQuery
        {
            PlayerPos = new PointF(38.1f, 113.6f),
            GoalPos = new PointF(36.9f, 106.0f),
            RopeSegments = Rope,
            Candidates = new List<ClimbEdgeCandidate>
            {
                new(climbUp, new PointF(38.6f, 105.9f)),
                new(climbDown, new PointF(38.6f, 122.8f))
            },
            RopeXTolerancePx = 1.5f,
            EndpointYTolerancePx = 1.5f
        };

        Assert.True(NavigationRopeHelper.TryPickClimbTowardGoal(query, out var result));
        Assert.Equal(NavigationActionType.ClimbUp, result.Edge.ActionType);
        Assert.Equal(105.9f, result.LandingPos.Y, precision: 1);
    }

    [Fact]
    public void TryPickClimbTowardGoal_NotOnRope_False()
    {
        var climbUp = new NavigationEdge("bot", "top", NavigationActionType.ClimbUp)
        {
            InputSequence = new List<string> { "ropeX:38.6" }
        };

        var query = new RopeClimbPickQuery
        {
            PlayerPos = new PointF(80f, 106f),
            GoalPos = new PointF(36.9f, 106.0f),
            RopeSegments = Rope,
            Candidates = new List<ClimbEdgeCandidate>
            {
                new(climbUp, new PointF(38.6f, 105.9f))
            },
            RopeXTolerancePx = 1.5f,
            EndpointYTolerancePx = 1.5f
        };

        Assert.False(NavigationRopeHelper.TryPickClimbTowardGoal(query, out _));
    }
}
