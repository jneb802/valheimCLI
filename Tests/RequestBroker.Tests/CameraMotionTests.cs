using Xunit;

namespace valheimCLI.Tests;

/// <summary>
/// The math and parsing behind cli_camera_arc and cli_camera_path: a cinematic move is only
/// smooth if it starts and ends at rest, keeps its radius, always faces its focus, and walks a
/// path at constant speed rather than at the spline's uneven parameter speed.
/// </summary>
public class CameraMotionTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void EaseStartsAndEndsAtRestAndIsSymmetric()
    {
        Assert.Equal(0, CameraMath.EaseInOut(0), 12);
        Assert.Equal(1, CameraMath.EaseInOut(1), 12);
        Assert.Equal(0.5, CameraMath.EaseInOut(0.5), 12);
        double h = 1e-5;
        Assert.True(CameraMath.EaseInOut(h) / h < 1e-3, "starts at rest");
        Assert.True((1 - CameraMath.EaseInOut(1 - h)) / h < 1e-3, "ends at rest");
        for (double t = 0; t <= 1; t += 0.05)
        {
            Assert.Equal(1 - CameraMath.EaseInOut(t), CameraMath.EaseInOut(1 - t), 9);
            Assert.True(CameraMath.EaseInOut(t + 0.01) >= CameraMath.EaseInOut(t), "never runs backwards");
        }

        Assert.Equal(0, CameraMath.EaseInOut(-1), 12);
        Assert.Equal(1, CameraMath.EaseInOut(2), 12);
        Assert.Equal(0.3, CameraMath.Ease(0.3, linear: true), 12);
    }

    [Theory]
    [InlineData(0, 0, 1, 0, 0)]     // north
    [InlineData(1, 0, 0, 90, 0)]    // east
    [InlineData(0, 0, -1, 180, 0)]  // south
    [InlineData(-1, 0, 0, -90, 0)]  // west
    [InlineData(0, -1, 1, 0, 45)]   // down 45 degrees: the game's pitch is positive downwards
    [InlineData(0, 1, 1, 0, -45)]
    public void YawPitchMatchesTheFreeFlyCamerasConvention(double x, double y, double z, double yaw, double pitch)
    {
        Assert.True(CameraMath.TryYawPitch(new CameraVector(x, y, z), out double gotYaw, out double gotPitch));
        Assert.Equal(yaw, gotYaw, 6);
        Assert.Equal(pitch, gotPitch, 6);
    }

    [Fact]
    public void DirectionInvertsYawPitch()
    {
        CameraVector dir = CameraMath.Direction(37, 22);
        Assert.Equal(1, dir.Length, 9);
        Assert.True(CameraMath.TryYawPitch(dir, out double yaw, out double pitch));
        Assert.Equal(37, yaw, 6);
        Assert.Equal(22, pitch, 6);
        Assert.False(CameraMath.TryYawPitch(new CameraVector(0, 0, 0), out _, out _));
    }

    [Fact]
    public void ArcKeepsRadiusAndHeightAndAlwaysFacesTheFocus()
    {
        CameraVector focus = new(5136, 66, -5408);
        CameraArc arc = new(focus, radius: 80, height: 30, startBearing: 180, sweep: 90, swing: false, linear: false);
        for (double t = 0; t <= 1.0001; t += 0.1)
        {
            CameraVector p = arc.PositionAt(t);
            Assert.Equal(80, CameraMath.HorizontalDistance(p, focus), 6);
            Assert.Equal(96, p.Y, 6);
            Assert.True(CameraMath.TryYawPitch(arc.LookAt(t) - p, out double yaw, out double pitch));
            // Looking at the focus: the yaw points back along the bearing, pitched down at atan(30/80).
            double expectedYaw = arc.BearingAt(t) + 180;
            Assert.Equal(0, Normalize(yaw - expectedYaw), 6);
            Assert.Equal(System.Math.Atan2(30, 80) * 180 / System.Math.PI, pitch, 6);
        }
    }

    [Fact]
    public void ArcStartsAtTheCameraAndPositiveDegreesMoveItToItsRight()
    {
        CameraVector focus = new(0, 0, 0);
        CameraVector camera = new(0, 10, -50); // south of the focus, looking north
        CameraArc arc = CameraArc.FromCamera(camera, focus, null, null, null, sweep: 90, swing: false, linear: false);
        Assert.Equal(50, arc.Radius, 9);
        Assert.Equal(10, arc.Height, 9);
        Assert.Equal(0, Normalize(arc.StartBearing - 180), 9);
        AssertNear(camera, arc.PositionAt(0));
        // Looking north, the camera's right is east: a quarter turn to the right ends east of the focus.
        AssertNear(new CameraVector(50, 10, 0), arc.PositionAt(1));
        Assert.True(arc.PositionAt(0.1).X > 0, "moves to the camera's right (east) from the start");
        CameraArc left = CameraArc.FromCamera(camera, focus, null, null, null, sweep: -90, swing: false, linear: false);
        AssertNear(new CameraVector(-50, 10, 0), left.PositionAt(1));
    }

    [Fact]
    public void ArcOverridesReplaceTheCamerasOwnValues()
    {
        CameraArc arc = CameraArc.FromCamera(new CameraVector(0, 10, -50), new CameraVector(0, 0, 0), radius: 20, height: 5, startBearing: 90, sweep: 10, swing: false, linear: true);
        AssertNear(new CameraVector(20, 5, 0), arc.PositionAt(0));
    }

    [Fact]
    public void ArcEasesSoTheSweepIsHalfDoneAtHalfTime()
    {
        CameraArc arc = new(new CameraVector(0, 0, 0), 10, 0, 0, 40, swing: false, linear: false);
        Assert.Equal(0, arc.OffsetAt(0), 9);
        Assert.Equal(20, arc.OffsetAt(0.5), 9);
        Assert.Equal(40, arc.OffsetAt(1), 9);
        Assert.True(arc.OffsetAt(0.1) < 4, "slower than linear at the start");
    }

    [Fact]
    public void SwingGoesLeftThenRightTwiceAsFarThenBackToTheCentre()
    {
        CameraArc arc = new(new CameraVector(0, 0, 0), 10, 0, 180, 20, swing: true, linear: false);
        Assert.Equal(0, arc.OffsetAt(0), 9);
        Assert.Equal(-20, arc.OffsetAt(0.25), 9);
        Assert.Equal(0, arc.OffsetAt(0.5), 9);
        Assert.Equal(20, arc.OffsetAt(0.75), 9);
        Assert.Equal(0, arc.OffsetAt(1), 9);
        double min = 0;
        double max = 0;
        for (double t = 0; t <= 1; t += 0.001)
        {
            double offset = arc.OffsetAt(t);
            min = System.Math.Min(min, offset);
            max = System.Math.Max(max, offset);
        }

        Assert.Equal(-20, min, 3);
        Assert.Equal(20, max, 3);
        // At rest at each turn: no jump in angle between neighbouring frames there.
        Assert.True(System.Math.Abs(arc.OffsetAt(0.2501) - arc.OffsetAt(0.2499)) < 1e-3);
    }

    [Fact]
    public void PathPassesThroughEveryWaypoint()
    {
        CameraVector[] points = { new(0, 50, 0), new(40, 55, 10), new(80, 60, -20), new(120, 50, 0) };
        CameraPath path = new(points, null, null, null, 8, linear: false);
        for (int i = 0; i < points.Length; i++)
        {
            AssertNear(points[i], CameraPath.Evaluate(points, i));
        }

        AssertNear(points[0], path.PositionAt(0));
        AssertNear(points[3], path.PositionAt(1));
    }

    [Fact]
    public void PathWalksItsLengthAtConstantSpeed()
    {
        // Uneven spacing: the spline parameter runs fast on the long leg and slow on the short one,
        // so an arc-length table is what keeps the camera's speed even.
        CameraVector[] points = { new(0, 0, 0), new(5, 0, 0), new(100, 0, 30), new(110, 0, 35) };
        CameraPath path = new(points, null, null, null, 8, linear: true);
        int steps = 200;
        double expected = path.Length / steps;
        CameraVector previous = path.PositionAt(0);
        for (int i = 1; i <= steps; i++)
        {
            CameraVector here = path.PositionAt((double)i / steps);
            double step = CameraVector.Distance(previous, here);
            Assert.True(System.Math.Abs(step - expected) < expected * 0.02, $"step {i}: {step} vs {expected}");
            previous = here;
        }
    }

    [Fact]
    public void ParameterAtDistanceInvertsTheArcLength()
    {
        CameraVector[] points = { new(0, 0, 0), new(30, 0, 0) };
        CameraPath path = new(points, null, null, null, 8, linear: true);
        Assert.Equal(30, path.Length, 6);
        Assert.Equal(0.5, path.ParameterAt(15), 3);
        AssertNear(new CameraVector(15, 0, 0), path.PointAtDistance(15), 1e-3);
        Assert.Equal(0, path.ParameterAt(-1), 9);
        Assert.Equal(1, path.ParameterAt(31), 9);
    }

    [Fact]
    public void PathEasesInAndOut()
    {
        CameraPath path = new(new[] { new CameraVector(0, 0, 0), new CameraVector(100, 0, 0) }, null, null, null, 8, linear: false);
        Assert.Equal(50, path.DistanceAt(0.5), 6);
        Assert.True(path.DistanceAt(0.01) < 0.1, "starts at rest");
        Assert.True(100 - path.DistanceAt(0.99) < 0.1, "ends at rest");
    }

    [Fact]
    public void PathLooksAheadAlongItselfAndPastTheEnd()
    {
        CameraPath path = new(new[] { new CameraVector(0, 10, 0), new CameraVector(0, 10, 100) }, null, null, null, 8, linear: true);
        Assert.True(CameraMath.TryYawPitch(path.LookAt(0.5) - path.PositionAt(0.5), out double yaw, out double pitch));
        Assert.Equal(0, yaw, 6);
        Assert.Equal(0, pitch, 6);
        Assert.True(CameraMath.TryYawPitch(path.LookAt(1) - path.PositionAt(1), out yaw, out pitch), "still has a heading at the end");
        Assert.Equal(0, yaw, 6);
    }

    [Fact]
    public void PathLookModes()
    {
        CameraVector[] points = { new(0, 40, 0), new(100, 40, 0) };
        CameraVector target = new(50, 0, 50);
        CameraPath fixedTarget = new(points, null, target, null, 8, linear: true);
        AssertNear(target, fixedTarget.LookAt(0.3));

        CameraPath pitched = new(points, null, null, 30, 8, linear: true);
        Assert.True(CameraMath.TryYawPitch(pitched.LookAt(0.5) - pitched.PositionAt(0.5), out double yaw, out double pitch));
        Assert.Equal(90, yaw, 6); // heading east
        Assert.Equal(30, pitch, 6);

        CameraVector[] looks = { new(0, 0, 50), new(100, 0, 50) };
        CameraPath perPoint = new(points, looks, null, null, 8, linear: true);
        AssertNear(looks[0], perPoint.LookAt(0));
        AssertNear(looks[1], perPoint.LookAt(1));
        AssertNear(new CameraVector(50, 0, 50), perPoint.LookAt(0.5), 1e-2);
    }

    [Fact]
    public void ArcRequestParsesFocusAndOptions()
    {
        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc 40 20 5136 66 -5408 --radius 90 --height 25 --start 200 --swing --linear"), out CameraArcRequest r, out _));
        Assert.Equal(40, r.Degrees);
        Assert.Equal(20, r.Seconds);
        Assert.True(r.Focus.HasValue);
        AssertNear(new CameraVector(5136, 66, -5408), r.Focus!.Value);
        Assert.Equal(90, r.Radius);
        Assert.Equal(25, r.Height);
        Assert.Equal(200, r.StartBearing);
        Assert.True(r.Swing);
        Assert.True(r.Linear);

        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc -20 12"), out r, out _));
        Assert.False(r.Focus.HasValue);
        Assert.False(r.Swing);
        Assert.Equal(-20, r.Degrees);

        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc 20 12 --distance 60 --swing"), out r, out _));
        Assert.Equal(60, r.Distance);
    }

    [Theory]
    [InlineData("cli_camera_arc")]
    [InlineData("cli_camera_arc 20")]
    [InlineData("cli_camera_arc x 10")]
    [InlineData("cli_camera_arc 0 10")]
    [InlineData("cli_camera_arc 20 0")]
    [InlineData("cli_camera_arc 20 -5")]
    [InlineData("cli_camera_arc 20 4000")]
    [InlineData("cli_camera_arc 20 10 1 2")]
    [InlineData("cli_camera_arc 20 10 1 2 z")]
    [InlineData("cli_camera_arc 20 10 --radius")]
    [InlineData("cli_camera_arc 20 10 --radius 0")]
    [InlineData("cli_camera_arc 20 10 --bogus")]
    [InlineData("cli_camera_arc 20 10 1 2 3 --distance 50")]
    [InlineData("cli_camera_arc NaN 10")]
    public void ArcRequestRefusesBadInput(string line)
    {
        Assert.False(CameraArcRequest.TryParse(Args(line), out _, out string error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void PathRequestParsesWaypointsAndLooks()
    {
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 30 5000,90,-5400 5100,95,-5420 5200,90,-5400 --pitch 25"), out CameraPathRequest r, out _));
        Assert.Equal(30, r.Seconds);
        Assert.Equal(3, r.Points.Count);
        AssertNear(new CameraVector(5100, 95, -5420), r.Points[1]);
        Assert.Equal(25, r.Pitch);
        Assert.Null(r.Looks);

        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 0,1,2@3,4,5 6,7,8@9,10,11"), out r, out _));
        Assert.NotNull(r.Looks);
        AssertNear(new CameraVector(9, 10, 11), r.Looks![1]);

        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 6,7,8 --from-here --look 1,2,3 --linear"), out r, out _));
        Assert.True(r.FromHere);
        Assert.True(r.Linear);
        Assert.Equal(8, r.LookAhead);
        AssertNear(new CameraVector(1, 2, 3), r.Target!.Value);

        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 1,2,3 6,7,8 --ahead 0"), out r, out _));
        Assert.Equal(0, r.LookAhead);
    }

    [Theory]
    [InlineData("cli_camera_path 10")]
    [InlineData("cli_camera_path 10 1,2,3")]
    [InlineData("cli_camera_path 0 1,2,3 4,5,6")]
    [InlineData("cli_camera_path 10 1,2 4,5,6")]
    [InlineData("cli_camera_path 10 1,2,3,4 4,5,6")]
    [InlineData("cli_camera_path 10 1,2,3@1,1,1 4,5,6")]
    [InlineData("cli_camera_path 10 1,2,3@1,1 4,5,6@1,1,1")]
    [InlineData("cli_camera_path 10 1,2,3@1,1,1 4,5,6@1,1,1 --look 1,1,1")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --look 1,1,1 --pitch 20")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --pitch 95")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --ahead -1")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --look")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --fast")]
    [InlineData("cli_camera_path 10 4,5,6@1,1,1 --from-here")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --look 1,1,1 --ahead 20")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --pitch 20 --ahead 20")]
    public void PathRequestRefusesBadInput(string line)
    {
        Assert.False(CameraPathRequest.TryParse(Args(line), out _, out string error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void PathCarriesByDefaultAndArcOnlyWhenAsked()
    {
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 1,2,3 4,5,6"), out CameraPathRequest p, out _));
        Assert.Equal(CameraCarry.Start, p.Options.Carry);
        Assert.Null(p.Options.Check);
        Assert.Null(p.Options.Clean);
        Assert.False(p.Options.NoSpawn);

        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 1,2,3 4,5,6 --no-carry"), out p, out _));
        Assert.Equal(CameraCarry.Off, p.Options.Carry);
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 1,2,3 --carry end 4,5,6"), out p, out _));
        Assert.Equal(CameraCarry.End, p.Options.Carry);
        Assert.Equal(2, p.Points.Count); // "end" is the carry's value; the next token is a waypoint again

        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc 20 10"), out CameraArcRequest a, out _));
        Assert.Equal(CameraCarry.Off, a.Options.Carry);
        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc 20 10 --carry"), out a, out _));
        Assert.Equal(CameraCarry.Start, a.Options.Carry);
        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc 20 10 1 2 3 --carry start --swing"), out a, out _));
        Assert.Equal(CameraCarry.Start, a.Options.Carry);
        Assert.True(a.Swing);
    }

    [Fact]
    public void CheckCleanAndNospawnTakeOptionalValues()
    {
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 1,2,3 4,5,6 --check"), out CameraPathRequest p, out _));
        Assert.Equal(1.0, p.Options.Check);
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 --check 2.5 1,2,3 4,5,6 --clean --nospawn"), out p, out _));
        Assert.Equal(2.5, p.Options.Check);
        Assert.Equal(40, p.Options.Clean);
        Assert.True(p.Options.NoSpawn);
        Assert.Equal(2, p.Points.Count);
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 1,2,3 4,5,6 --clean 120"), out p, out _));
        Assert.Equal(120, p.Options.Clean);

        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc 20 10 --check 0.5 --clean 30 --nospawn --linear"), out CameraArcRequest a, out _));
        Assert.Equal(0.5, a.Options.Check);
        Assert.Equal(30, a.Options.Clean);
        Assert.True(a.Options.NoSpawn);
        Assert.True(a.Linear);
        Assert.Equal("carry=off clean=30m nospawn=on end=player", a.Options.Describe());
    }

    [Theory]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --carry --no-carry")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --no-carry --carry")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --check 0")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --check 25")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --clean -1")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --clean 5000")]
    [InlineData("cli_camera_arc 20 10 --carry --no-carry")]
    [InlineData("cli_camera_arc 20 10 --check 0")]
    [InlineData("cli_camera_arc 20 10 --carry middle")]
    public void MoveOptionsRefuseBadInput(string line)
    {
        bool ok = line.StartsWith("cli_camera_arc", StringComparison.Ordinal)
            ? CameraArcRequest.TryParse(Args(line), out _, out string error)
            : CameraPathRequest.TryParse(Args(line), out _, out error);
        Assert.False(ok);
        Assert.StartsWith("ERROR", error);
    }

    [Fact]
    public void CleanRequestTakesRadiusCentreAndAll()
    {
        Assert.True(CameraCleanRequest.TryParse(Args("cli_camera_clean"), out CameraCleanRequest r, out _));
        Assert.Equal(CameraCleanRequest.DefaultRadius, r.Radius);
        Assert.Null(r.Centre);
        Assert.False(r.All);
        Assert.True(CameraCleanRequest.TryParse(Args("cli_camera_clean 80"), out r, out _));
        Assert.Equal(80, r.Radius);
        Assert.True(CameraCleanRequest.TryParse(Args("cli_camera_clean 5136 66 -5408 --all"), out r, out _));
        Assert.Equal(CameraCleanRequest.DefaultRadius, r.Radius);
        AssertNear(new CameraVector(5136, 66, -5408), r.Centre!.Value);
        Assert.True(r.All);
        Assert.True(CameraCleanRequest.TryParse(Args("cli_camera_clean 30 1 2 3"), out r, out _));
        Assert.Equal(30, r.Radius);
        AssertNear(new CameraVector(1, 2, 3), r.Centre!.Value);
    }

    [Theory]
    [InlineData("cli_camera_clean 1 2")]
    [InlineData("cli_camera_clean 1 2 3 4 5")]
    [InlineData("cli_camera_clean 0")]
    [InlineData("cli_camera_clean 2000")]
    [InlineData("cli_camera_clean --bogus")]
    [InlineData("cli_camera_clean 40 x")]
    public void CleanRequestRefusesBadInput(string line)
    {
        Assert.False(CameraCleanRequest.TryParse(Args(line), out _, out string error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void PathSamplesAreEvenAndAtMostTheStepApart()
    {
        CameraVector[] points = { new(0, 40, 0), new(30, 42, 10), new(60, 40, 0), new(90.5, 41, -12) };
        CameraPath path = new(points, null, null, null, 8, linear: false);
        List<CameraMoveSample> samples = path.Samples(1.0);
        Assert.Equal(Math.Ceiling(path.Length), samples.Count - 1);
        AssertNear(points[0], samples[0].Position);
        AssertNear(points[^1], samples[^1].Position, 1e-6);
        Assert.Equal(0, samples[0].Parameter, 9);
        Assert.Equal(points.Length - 1, samples[^1].Parameter, 9);
        for (int i = 1; i < samples.Count; i++)
        {
            Assert.True(samples[i].Parameter > samples[i - 1].Parameter, "the spline parameter only grows");
            double chord = CameraVector.Distance(samples[i].Position, samples[i - 1].Position);
            Assert.True(chord <= 1.0 + 1e-6, $"step {chord} over 1 m");
            Assert.Equal(path.Length / (samples.Count - 1), samples[i].Along - samples[i - 1].Along, 9);
        }

        Assert.Equal(1, CameraMoveSample.Steps(0, 1));
        Assert.Equal(3, CameraMoveSample.Steps(3, 1));
        Assert.Equal(4, CameraMoveSample.Steps(3.01, 1));
    }

    [Fact]
    public void ArcSamplesCoverTheWholeSwing()
    {
        CameraArc arc = new(new CameraVector(0, 0, 0), 50, 10, 0, 30, swing: true, linear: false);
        List<CameraMoveSample> samples = arc.Samples(1.0);
        Assert.Equal(-30, samples[0].Parameter, 9);
        Assert.Equal(30, samples[^1].Parameter, 9);
        double arcLength = 60 * Math.PI / 180 * 50;
        Assert.Equal(Math.Ceiling(arcLength), samples.Count - 1);
        foreach (CameraMoveSample s in samples)
        {
            Assert.Equal(50, CameraMath.HorizontalDistance(s.Position, arc.Focus), 6);
            Assert.Equal(10, s.Position.Y, 9);
        }

        // Every pose the move takes lies on the sampled stretch of the circle.
        for (double t = 0; t <= 1; t += 0.01)
        {
            Assert.InRange(arc.OffsetAt(t), samples[0].Parameter - 1e-9, samples[^1].Parameter + 1e-9);
        }

        CameraArc left = new(new CameraVector(0, 0, 0), 50, 10, 0, -20, swing: false, linear: false);
        List<CameraMoveSample> l = left.Samples(1.0);
        Assert.Equal(-20, l[0].Parameter, 9);
        Assert.Equal(0, l[^1].Parameter, 9);
        AssertNear(left.PositionAt(0), l[^1].Position, 1e-6);
        AssertNear(left.PositionAt(1), l[0].Position, 1e-6);
    }

    [Theory]
    [InlineData(3600, false, 0, 360)]
    [InlineData(-720, false, -360, 0)]
    [InlineData(200, true, -200, 160)]
    public void ArcSamplesStopAtOneRevolution(double sweep, bool swing, double from, double to)
    {
        CameraArc arc = new(new CameraVector(0, 0, 0), 20, 5, 30, sweep, swing, linear: false);
        List<CameraMoveSample> samples = arc.Samples(1.0);
        Assert.Equal(from, samples[0].Parameter, 9);
        Assert.Equal(to, samples[^1].Parameter, 9);
        Assert.Equal(Math.Ceiling(2 * Math.PI * 20), samples.Count - 1);
        AssertNear(arc.PositionAt(0), arc.PositionAtOffset(0));
    }

    [Fact]
    public void CarryGivenTellsAnAskedCarryFromTheDefault()
    {
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 1,2,3 4,5,6"), out CameraPathRequest p, out _));
        Assert.False(p.Options.CarryGiven);
        Assert.Equal("carry=start clean=off nospawn=off end=player", p.Options.Describe());
        Assert.Equal("carry=off clean=off nospawn=off end=player", p.Options.Describe(CameraCarry.Off));
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 1,2,3 4,5,6 --carry"), out p, out _));
        Assert.True(p.Options.CarryGiven);
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 1,2,3 4,5,6 --no-carry"), out p, out _));
        Assert.True(p.Options.CarryGiven);
        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc 20 10"), out CameraArcRequest a, out _));
        Assert.False(a.Options.CarryGiven);
        Assert.Equal("carry=end clean=off nospawn=off end=player", a.Options.Describe(CameraCarry.End));
    }

    [Fact]
    public void NearAnyMeasuresToTheClosestSample()
    {
        List<CameraMoveSample> line = new() { new(0, 0, new CameraVector(0, 0, 0)), new(1, 10, new CameraVector(10, 0, 0)) };
        Assert.True(CameraMoveSample.NearAny(new CameraVector(10, 3, 0), line, 3));
        Assert.False(CameraMoveSample.NearAny(new CameraVector(5, 4.9, 0), line, 3));
        Assert.False(CameraMoveSample.NearAny(new CameraVector(0, 0, 0), new List<CameraMoveSample>(), 100));
    }

    [Fact]
    public void ClearanceMergesRunsOfOneKindAcrossShortGaps()
    {
        List<ClearanceSample> samples = new();
        // 0..30 m at 1 m: clips at 3-5 and 7 (a 2 m gap: one stretch), at 12 (alone), unloaded
        // 20-30 with a clip at 24 inside it; 24 is too far from 12 to join that clip stretch.
        for (int i = 0; i <= 30; i++)
        {
            CameraMoveSample s = new(i / 10.0, i, new CameraVector(i, 0, 0));
            if ((i >= 3 && i <= 5) || i == 7) samples.Add(new ClearanceSample(s, ClearanceKind.Clip, i == 4 ? "rock4_frac" : "terrain", i == 4 ? 0.2 : 0.6));
            else if (i == 12 || i == 24) samples.Add(new ClearanceSample(s, ClearanceKind.Clip, "piece_wall", 0));
            else if (i >= 20) samples.Add(new ClearanceSample(s, ClearanceKind.Unloaded));
            else samples.Add(new ClearanceSample(s, ClearanceKind.Clear));
        }

        List<ClearanceStretch> stretches = CameraClearance.Merge(samples);
        Assert.Equal(4, stretches.Count);

        Assert.Equal(ClearanceKind.Clip, stretches[0].Kind);
        Assert.Equal(3, stretches[0].First.Sample.Along);
        Assert.Equal(7, stretches[0].Last.Sample.Along);
        Assert.Equal(4, stretches[0].Samples);
        Assert.Equal(4, stretches[0].Closest.Sample.Along);
        Assert.Equal(new[] { "terrain", "rock4_frac" }, stretches[0].Colliders);

        Assert.Equal(12, stretches[1].First.Sample.Along);
        Assert.Equal(12, stretches[1].Last.Sample.Along);

        // The unloaded run stays one stretch: the clip at 24 is the clips' and leaves only a 2 m gap.
        Assert.Equal(ClearanceKind.Unloaded, stretches[2].Kind);
        Assert.Equal(20, stretches[2].First.Sample.Along);
        Assert.Equal(30, stretches[2].Last.Sample.Along);
        Assert.Equal(10, stretches[2].Samples);

        Assert.Equal(ClearanceKind.Clip, stretches[3].Kind);
        Assert.Equal(24, stretches[3].First.Sample.Along);

        string line = CameraClearance.Format(stretches[0], "u");
        Assert.StartsWith("CLIP u=0.300..0.700 s=3.0..7.0m samples=4 from=3.00,0.00,0.00 to=7.00,0.00,0.00", line);
        Assert.Contains("closest u=0.400 s=4.0m at=4.00,0.00,0.00 collider=rock4_frac dist=0.20 colliders=terrain,rock4_frac", line);
        Assert.Equal("UNLOADED u=2.000..3.000 s=20.0..30.0m samples=10 from=20.00,0.00,0.00 to=30.00,0.00,0.00", CameraClearance.Format(stretches[2], "u"));
    }

    [Fact]
    public void ClearanceSplitsRunsFurtherApartThanTheGap()
    {
        List<ClearanceSample> samples = new();
        foreach (double along in new[] { 0.0, 1.0, 3.5, 4.0 })
        {
            samples.Add(new ClearanceSample(new CameraMoveSample(along, along, new CameraVector(along, 0, 0)), ClearanceKind.Clip, "terrain", 0.5));
        }

        List<ClearanceStretch> stretches = CameraClearance.Merge(samples);
        Assert.Equal(2, stretches.Count);
        Assert.Equal(1.0, stretches[0].Last.Sample.Along);
        Assert.Equal(3.5, stretches[1].First.Sample.Along);
        Assert.Single(CameraClearance.Merge(samples, mergeGap: 2.5));
        Assert.Empty(CameraClearance.Merge(new List<ClearanceSample>()));
    }

    [Fact]
    public void StayIsAMoveOptionOnBothMoves()
    {
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 1,2,3 4,5,6"), out CameraPathRequest p, out _));
        Assert.False(p.Options.Stay);
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 1,2,3 --stay 4,5,6 --carry end"), out p, out _));
        Assert.True(p.Options.Stay);
        Assert.Equal(2, p.Points.Count);
        Assert.Equal(CameraCarry.End, p.Options.Carry);
        Assert.Equal("carry=end clean=off nospawn=off end=stay", p.Options.Describe());
        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc 20 10 --swing --stay"), out CameraArcRequest a, out _));
        Assert.True(a.Options.Stay);
        Assert.True(a.Swing);
    }

    [Fact]
    public void StopRequestTakesOnlyStay()
    {
        Assert.True(CameraStopRequest.TryParse(Args("cli_camera_stop"), out CameraStopRequest r, out _));
        Assert.False(r.Stay);
        Assert.True(CameraStopRequest.TryParse(Args("cli_camera_stop --STAY"), out r, out _));
        Assert.True(r.Stay);
        Assert.False(CameraStopRequest.TryParse(Args("cli_camera_stop now"), out _, out string error));
        Assert.StartsWith("ERROR", error);
    }

    [Theory]
    [InlineData(CameraMoveEnd.Finished, false, true)]
    [InlineData(CameraMoveEnd.Finished, true, false)]
    [InlineData(CameraMoveEnd.Stopped, false, true)]
    [InlineData(CameraMoveEnd.Stopped, true, false)]
    [InlineData(CameraMoveEnd.Failed, false, true)]
    [InlineData(CameraMoveEnd.Failed, true, false)]
    // A manual pose (cli_freefly_pose, cli_capture) wants free-fly; a replacing move keeps it;
    // free-fly turned off under the move or a logout save leaves nothing to give back.
    [InlineData(CameraMoveEnd.ManualPose, false, false)]
    [InlineData(CameraMoveEnd.Replaced, false, false)]
    [InlineData(CameraMoveEnd.Interrupted, false, false)]
    public void MovesEndByReturningTheViewToThePlayerUnlessTheyStay(CameraMoveEnd end, bool stay, bool returns)
    {
        Assert.Equal(returns, CameraMoveEnding.ReturnsToPlayer(end, stay));
    }

    private static string[] Args(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static double Normalize(double degrees)
    {
        double d = degrees % 360;
        if (d > 180) d -= 360;
        if (d < -180) d += 360;
        return d;
    }

    private static void AssertNear(CameraVector expected, CameraVector actual, double tolerance = Tolerance)
    {
        Assert.True(CameraVector.Distance(expected, actual) <= tolerance, $"expected {expected.Format()} got {actual.Format()}");
    }
}
