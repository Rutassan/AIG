using System.Reflection;
using AIG.Game.Config;
using AIG.Game.Core;
using AIG.Game.Cosmos;
using AIG.Game.Tests.Fakes;
using AIG.Game.World;
using System.Numerics;

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
        Assert.Equal(36d, appUniverse.SimulationTimeSeconds, 6);
    }

    [Fact(DisplayName = "GameApp считает day/night от звезды и вращения активной планеты")]
    public void GameApp_AstronomicalLightingState_TracksDayNightFromUniverse()
    {
        var universe = CreateLightingUniverse();
        var app = CreateLightingApp(universe);

        var day = app.GetAstronomicalLightingState(new Vector3(16f, 3f, 16f));
        universe.AdvanceTime(5d);
        var night = app.GetAstronomicalLightingState(new Vector3(16f, 3f, 16f));

        Assert.True(day.SunAltitude > 0.7f);
        Assert.True(day.DaylightFactor > 0.9f);
        Assert.True(day.SunIlluminance > 0.9f);
        Assert.True(night.SunAltitude < -0.7f);
        Assert.True(night.NightFactor > 0.9f);
        Assert.True(night.SunIlluminance < 0.1f);
    }

    [Fact(DisplayName = "GameApp planet frame и universe position привязывают локальный мир к активной планете")]
    public void GameApp_PlanetFrame_MapsLocalWorldToActivePlanet()
    {
        var universe = CreateLightingUniverse();
        var app = CreateLightingApp(universe);

        var frame = app.GetPlanetFrame(new Vector3(16f, 2f, 16f));
        var centerUniversePosition = app.GetUniversePositionForLocalPoint(new Vector3(16f, 2f, 16f));
        var offsetUniversePosition = app.GetUniversePositionForLocalPoint(new Vector3(18f, 2f, 20f));

        Assert.NotNull(frame.Body);
        Assert.Equal(CelestialBodyKind.Planet, frame.Body!.Kind);
        Assert.True(frame.Up.Length > 0.99d);
        Assert.True((centerUniversePosition - frame.SurfaceOrigin).Length > 1.5d);
        Assert.NotEqual(centerUniversePosition, offsetUniversePosition);
    }

    [Fact(DisplayName = "GameApp выбирает первый не-star body, если в системе нет планеты")]
    public void GameApp_PlanetFrame_FallsBackToFirstNonStarBody()
    {
        var star = new CelestialBody("Helios", CelestialBodyKind.Star, 100d, 10d);
        _ = new CelestialBody("Relay", CelestialBodyKind.Moon, 10d, 3d, star, new OrbitParameters(12d, 30d));
        var app = CreateLightingApp(new Universe("MoonOnly", 1, [new StarSystem("Home", Vector3d.Zero, star)]));

        var frame = app.GetPlanetFrame(new Vector3(16f, 2f, 16f));

        Assert.NotNull(frame.Body);
        Assert.Equal(CelestialBodyKind.Moon, frame.Body!.Kind);
    }

    [Fact(DisplayName = "GameApp использует fallback планетарного кадра и света без активной системы")]
    public void GameApp_AstronomicalLightingState_FallsBackWithoutActiveSystem()
    {
        var app = CreateLightingApp(new Universe("Empty", 1, []));

        var frame = app.GetPlanetFrame(new Vector3(3f, 2f, 4f));
        var universePosition = app.GetUniversePositionForLocalPoint(new Vector3(3f, 2f, 4f));
        var lighting = app.GetAstronomicalLightingState(new Vector3(3f, 2f, 4f));

        Assert.Null(frame.Body);
        Assert.Null(frame.System);
        Assert.Equal(new Vector3d(0d, 1d, 0d), frame.Up);
        Assert.Equal(new Vector3d(3d, 2d, 4d), universePosition);
        Assert.Null(lighting.ActiveBody);
        Assert.Null(lighting.ActiveSystem);
        Assert.Equal(1f, lighting.DaylightFactor);
        Assert.Equal(1f, lighting.SunIlluminance);
        Assert.Equal(1f, lighting.SkyIlluminance);
    }

    [Fact(DisplayName = "GameApp использует fallback света при вырожденном направлении на звезду")]
    public void GameApp_AstronomicalLightingState_FallsBackForDegenerateStarDirection()
    {
        var star = new CelestialBody("Helios", CelestialBodyKind.Star, 100d, 10d);
        _ = new CelestialBody(
            "AIG-Prime",
            CelestialBodyKind.Planet,
            10d,
            10d,
            parent: star,
            orbit: new OrbitParameters(5d, 100d, initialPhaseRadians: 0d),
            rotationPeriodSeconds: 0d);
        var universe = new Universe("Degenerate", 1, [new StarSystem("Home", Vector3d.Zero, star)]);
        var app = CreateLightingApp(universe);

        var lighting = app.GetAstronomicalLightingState(new Vector3(15.5f, -15f, 15.5f));

        Assert.True(lighting.DaylightFactor > 0.99f);
        Assert.True(lighting.SunIlluminance > 0.99f);
        Assert.True(lighting.SkyIlluminance > 0.99f);
    }

    private static GameApp CreateLightingApp(Universe universe)
    {
        return new GameApp(
            new GameConfig { FullscreenByDefault = false },
            new FakeGamePlatform(),
            new WorldMap(width: 32, height: 16, depth: 32, chunkSize: 8, seed: 0),
            universe: universe);
    }

    private static Universe CreateLightingUniverse()
    {
        var star = new CelestialBody(
            name: "Helios",
            kind: CelestialBodyKind.Star,
            mass: 1.98847e30d,
            radius: 696_340_000d);
        var planet = new CelestialBody(
            name: "AIG-Prime",
            kind: CelestialBodyKind.Planet,
            mass: 5.9722e24d,
            radius: 6_371_000d,
            parent: star,
            orbit: new OrbitParameters(149_597_870_700d, 31_557_600d, initialPhaseRadians: Math.PI),
            rotationPeriodSeconds: 10d);

        return new Universe("Lighting", 1, [new StarSystem("Home", Vector3d.Zero, star)]);
    }
}
