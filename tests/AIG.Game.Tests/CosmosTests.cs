using System.Reflection;
using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.Cosmos;
using AIG.Game.Tests.Fakes;
using AIG.Game.World;

namespace AIG.Game.Tests;

public sealed class CosmosTests
{
    [Fact(DisplayName = "Vector3d нормализует обычный вектор и безопасно обрабатывает нуль и деление на ноль")]
    public void Vector3d_Normalize_AndDivide_HandleEdgeCases()
    {
        var normalized = new Vector3d(3d, 0d, 4d).Normalize();
        var zeroNormalized = Vector3d.Zero.Normalize();
        var dividedByZero = new Vector3d(1d, 2d, 3d) / 0d;
        var scaled = 2d * new Vector3d(1d, 2d, 3d);

        Assert.Equal(0.6d, normalized.X, 6);
        Assert.Equal(0d, normalized.Y, 6);
        Assert.Equal(0.8d, normalized.Z, 6);
        Assert.Equal(Vector3d.Zero, zeroNormalized);
        Assert.Equal(Vector3d.Zero, dividedByZero);
        Assert.Equal(new Vector3d(2d, 4d, 6d), scaled);
    }

    [Fact(DisplayName = "OrbitParameters валидирует полуось и период")]
    public void OrbitParameters_ValidateInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrbitParameters(0d, 1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrbitParameters(1d, 0d));

        var orbit = new OrbitParameters(12d, 34d, 0.5d, 1.2d);
        Assert.Equal(12d, orbit.SemiMajorAxis);
        Assert.Equal(34d, orbit.OrbitalPeriodSeconds);
        Assert.Equal(0.5d, orbit.InclinationRadians);
        Assert.Equal(1.2d, orbit.InitialPhaseRadians);
    }

    [Fact(DisplayName = "CelestialBody валидирует параметры и строит иерархию орбит")]
    public void CelestialBody_ValidatesInputs_AndBuildsHierarchy()
    {
        var star = new CelestialBody("Helios", CelestialBodyKind.Star, 10d, 5d);

        Assert.Throws<ArgumentException>(() => new CelestialBody("", CelestialBodyKind.Star, 10d, 5d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CelestialBody("Bad", CelestialBodyKind.Planet, 0d, 5d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CelestialBody("Bad", CelestialBodyKind.Planet, 10d, 0d));
        Assert.Throws<ArgumentException>(() => new CelestialBody("Bad", CelestialBodyKind.Planet, 10d, 5d, parent: star));
        Assert.Throws<ArgumentException>(() => new CelestialBody("Bad", CelestialBodyKind.Planet, 10d, 5d, orbit: new OrbitParameters(10d, 20d)));

        var planet = new CelestialBody(
            "AIG-Prime",
            CelestialBodyKind.Planet,
            20d,
            6d,
            parent: star,
            orbit: new OrbitParameters(100d, 40d));

        Assert.Equal("Helios", star.Name);
        Assert.Single(star.Children);
        Assert.Same(planet, star.Children[0]);
    }

    [Fact(DisplayName = "CelestialBody считает абсолютную позицию по орбите и rotation angle")]
    public void CelestialBody_ComputesAbsolutePosition_AndRotationAngle()
    {
        var star = new CelestialBody("Helios", CelestialBodyKind.Star, 10d, 5d, rotationPeriodSeconds: 0d, initialRotationRadians: -0.4d);
        var planet = new CelestialBody(
            "AIG-Prime",
            CelestialBodyKind.Planet,
            20d,
            6d,
            parent: star,
            orbit: new OrbitParameters(100d, 40d, inclinationRadians: Math.PI / 6d),
            rotationPeriodSeconds: 10d,
            initialRotationRadians: 0.2d);

        var starPosition = star.GetAbsolutePosition(0d, new Vector3d(5d, 6d, 7d));
        var planetPosition = planet.GetAbsolutePosition(10d, Vector3d.Zero);
        var stillRotation = star.GetRotationAngle(25d);
        var spunRotation = planet.GetRotationAngle(5d);

        Assert.Equal(new Vector3d(5d, 6d, 7d), starPosition);
        Assert.NotEqual(Vector3d.Zero, planetPosition);
        Assert.InRange(stillRotation, 0d, Math.PI * 2d);
        Assert.InRange(spunRotation, 0d, Math.PI * 2d);
    }

    [Fact(DisplayName = "CelestialBody покрывает root и planar orbit ветки")]
    public void CelestialBody_CoversRootAndPlanarOrbitBranches()
    {
        var star = new CelestialBody("Helios", CelestialBodyKind.Star, 10d, 5d);
        var planet = new CelestialBody(
            "AIG-Prime",
            CelestialBodyKind.Planet,
            20d,
            6d,
            parent: star,
            orbit: new OrbitParameters(50d, 20d));
        var relativeOrbit = typeof(CelestialBody).GetMethod("GetRelativeOrbitPosition", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var rootRelative = Assert.IsType<Vector3d>(relativeOrbit.Invoke(star, [0d]));
        var planarRelative = Assert.IsType<Vector3d>(relativeOrbit.Invoke(planet, [5d]));

        Assert.Equal(Vector3d.Zero, rootRelative);
        Assert.Equal(0d, planarRelative.Y, 6);
    }

    [Fact(DisplayName = "StarSystem валидирует корневую звезду и разворачивает все тела")]
    public void StarSystem_ValidatesRootStar_AndFlattensBodies()
    {
        var fakeRoot = new CelestialBody("Root", CelestialBodyKind.Planet, 10d, 5d);
        Assert.Throws<ArgumentException>(() => new StarSystem("Bad", Vector3d.Zero, fakeRoot));
        Assert.Throws<ArgumentException>(() => new StarSystem("", Vector3d.Zero, new CelestialBody("Star", CelestialBodyKind.Star, 10d, 5d)));

        var star = new CelestialBody("Helios", CelestialBodyKind.Star, 10d, 5d);
        var planet = new CelestialBody("AIG-Prime", CelestialBodyKind.Planet, 20d, 6d, star, new OrbitParameters(100d, 40d));
        _ = new CelestialBody("AIG-Luna", CelestialBodyKind.Moon, 3d, 2d, planet, new OrbitParameters(10d, 5d));
        var system = new StarSystem("Home", new Vector3d(1d, 2d, 3d), star);

        Assert.Equal("Home", system.Name);
        Assert.Same(star, system.Star);
        Assert.Equal(3, system.Bodies.Count);
        Assert.Throws<ArgumentException>(() => system.GetAbsolutePosition(new CelestialBody("Other", CelestialBodyKind.Star, 1d, 1d), 0d));
        var orbitingStar = new CelestialBody("ChildStar", CelestialBodyKind.Star, 4d, 2d, star, new OrbitParameters(12d, 5d));
        Assert.Throws<ArgumentException>(() => new StarSystem("BadOrbitingStar", Vector3d.Zero, orbitingStar));
    }

    [Fact(DisplayName = "Universe поддерживает время, доминирующее тело и гравитационный вектор")]
    public void Universe_AdvancesTime_AndComputesGravity()
    {
        var empty = new Universe("Empty", 1, []);
        empty.AdvanceTime(-1d);
        Assert.Equal(0d, empty.SimulationTimeSeconds);
        Assert.False(empty.TryGetDominantBody(Vector3d.Zero, out var emptyBody));
        Assert.Null(emptyBody);
        Assert.Null(empty.GetDominantBody(Vector3d.Zero));
        Assert.Equal(Vector3d.Zero, empty.GetGravityAt(Vector3d.Zero));
        Assert.Throws<ArgumentException>(() => new Universe("", 1, []));
        Assert.Throws<ArgumentNullException>(() => new Universe("Bad", 1, null!));

        var universe = Universe.CreateDefault(777);
        universe.AdvanceTime(60d);

        Assert.Equal(60d, universe.SimulationTimeSeconds);
        Assert.Single(universe.StarSystems);
        Assert.Equal("AIG Universe", universe.Name);
        Assert.Equal(777, universe.Seed);

        var system = universe.StarSystems[0];
        var planet = Assert.Single(system.Bodies.Where(body => body.Kind == CelestialBodyKind.Planet));
        var planetPosition = system.GetAbsolutePosition(planet, universe.SimulationTimeSeconds);
        var samplePoint = planetPosition + new Vector3d(planet.Radius + 1000d, 0d, 0d);

        Assert.True(universe.TryGetDominantBody(samplePoint, out var dominant));
        Assert.Same(planet, dominant);
        Assert.Same(planet, universe.GetDominantBody(samplePoint));

        var gravity = universe.GetGravityAt(samplePoint);
        Assert.True(gravity.X < 0d);
        Assert.NotEqual(Vector3d.Zero, gravity);

        var zeroGravity = universe.GetGravityAt(planetPosition);
        Assert.NotEqual(Vector3d.Zero, zeroGravity);
    }

    [Fact(DisplayName = "Universe покрывает zero-offset ветку в ComputeGravityVector")]
    public void Universe_ComputeGravityVector_ReturnsZeroForZeroOffset()
    {
        var method = typeof(Universe).GetMethod("ComputeGravityVector", BindingFlags.Static | BindingFlags.NonPublic)!;
        var body = new CelestialBody("Body", CelestialBodyKind.Planet, 10d, 5d);

        var gravity = Assert.IsType<Vector3d>(method.Invoke(null, [body, Vector3d.Zero, Vector3d.Zero]));
        Assert.Equal(Vector3d.Zero, gravity);
    }

    [Fact(DisplayName = "GameApp продвигает simulation time universe вместе с runtime")]
    public void GameApp_AdvanceRuntime_AlsoAdvancesUniverse()
    {
        var universe = Universe.CreateDefault(777);
        var app = new GameApp(
            new GameConfig { FullscreenByDefault = false },
            new FakeGamePlatform(),
            new WorldMap(width: 16, height: 8, depth: 16, chunkSize: 8, seed: 0),
            universe: universe);

        var advanceRuntime = typeof(GameApp).GetMethod("AdvanceRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var universeProperty = typeof(GameApp).GetProperty("Universe", BindingFlags.Instance | BindingFlags.NonPublic)!;

        advanceRuntime.Invoke(app, [0.05f]);
        var appUniverse = Assert.IsType<Universe>(universeProperty.GetValue(app));

        Assert.Same(universe, appUniverse);
        Assert.Equal(0.05d, appUniverse.SimulationTimeSeconds, 6);
    }
}
