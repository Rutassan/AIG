using System.Numerics;
using AIG.Game.Bot;
using Raylib_cs;

namespace AIG.Game.Core;

internal enum BotWristDeviceTarget
{
    None = 0,
    Gather,
    BuildHouse,
    Cancel,
    Close,
    Wood,
    Stone,
    Dirt,
    Leaves,
    Amount,
    Confirm,
    Back
}

internal readonly record struct BotWristDeviceRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public Vector2 Center => new(X + W * 0.5f, Y + H * 0.5f);

    public bool Contains(Vector2 point)
    {
        return point.X >= X
            && point.X <= Right
            && point.Y >= Y
            && point.Y <= Bottom;
    }

    public (int X, int Y, int W, int H) ToTuple() => (X, Y, W, H);
}

internal readonly record struct BotWristDeviceElement(
    BotWristDeviceTarget Target,
    BotWristDeviceRect ScreenRect,
    Vector3 WorldCenter,
    float WorldWidth,
    float WorldHeight,
    bool Interactive);

internal sealed class BotWristDeviceLayout
{
    internal const int VirtualPanelWidthPixels = 372;
    internal const int VirtualPanelHeightPixels = 480;
    private const float PanelWorldWidthUnits = 0.56f;
    private const float PanelThickness = 0.02f;

    private readonly Dictionary<BotWristDeviceTarget, BotWristDeviceElement> _elements = [];

    private BotWristDeviceLayout(
        BotWristDeviceScreen screen,
        BotResourceType selectedResource,
        Camera3D camera,
        Vector3 panelCenter,
        Vector3 panelForward,
        Vector3 panelRight,
        Vector3 panelUp,
        BotWristDeviceRect panelRect)
    {
        Screen = screen;
        SelectedResource = selectedResource;
        Camera = camera;
        PanelCenter = panelCenter;
        PanelForward = panelForward;
        PanelRight = panelRight;
        PanelUp = panelUp;
        PanelRect = panelRect;
        PanelWorldHeight = PanelWorldWidthUnits * VirtualPanelHeightPixels / VirtualPanelWidthPixels;
    }

    public BotWristDeviceScreen Screen { get; }
    public BotResourceType SelectedResource { get; }
    public Camera3D Camera { get; }
    public Vector3 PanelCenter { get; }
    public Vector3 PanelForward { get; }
    public Vector3 PanelRight { get; }
    public Vector3 PanelUp { get; }
    public float PanelWorldHeight { get; }
    public float PanelWorldWidth => PanelWorldWidthUnits;
    public float Thickness => PanelThickness;
    public BotWristDeviceRect PanelRect { get; }
    public IReadOnlyCollection<BotWristDeviceElement> Elements => _elements.Values;

    public static BotWristDeviceLayout Create(
        Camera3D camera,
        int screenWidth,
        int screenHeight,
        BotWristDeviceState state,
        Vector3 panelCenter,
        Vector3 panelForward,
        Vector3 panelRight,
        Vector3 panelUp)
    {
        var panelRect = ProjectRect(
            camera,
            screenWidth,
            screenHeight,
            panelCenter,
            panelRight,
            panelUp,
            PanelWorldWidthUnits,
            PanelWorldWidthUnits * VirtualPanelHeightPixels / VirtualPanelWidthPixels);

        var layout = new BotWristDeviceLayout(
            state.Screen,
            state.SelectedResource,
            camera,
            panelCenter,
            panelForward,
            panelRight,
            panelUp,
            panelRect);

        layout.BuildElements();
        return layout;
    }

    public BotWristDeviceTarget HitTest(Vector2 mouse)
    {
        foreach (var element in _elements.Values)
        {
            if (element.Interactive && IsVisible(element.Target) && element.ScreenRect.Contains(mouse))
            {
                return element.Target;
            }
        }

        return BotWristDeviceTarget.None;
    }

    public BotWristDeviceRect GetRect(BotWristDeviceTarget target)
    {
        return _elements.TryGetValue(target, out var element)
            ? element.ScreenRect
            : new BotWristDeviceRect(0, 0, 0, 0);
    }

    public Vector3 GetTapTarget(BotWristDeviceTarget target)
    {
        return _elements.TryGetValue(target, out var element)
            ? element.WorldCenter + PanelForward * (Thickness * 0.85f)
            : PanelCenter + PanelForward * (Thickness * 0.85f);
    }

    public BotWristDeviceElement GetElement(BotWristDeviceTarget target)
    {
        return _elements[target];
    }

    public bool IsVisible(BotWristDeviceTarget target)
    {
        return (Screen, target) switch
        {
            (BotWristDeviceScreen.Main, BotWristDeviceTarget.Gather or BotWristDeviceTarget.BuildHouse or BotWristDeviceTarget.Cancel or BotWristDeviceTarget.Close) => true,
            (BotWristDeviceScreen.GatherResource, BotWristDeviceTarget.Wood or BotWristDeviceTarget.Stone or BotWristDeviceTarget.Dirt or BotWristDeviceTarget.Leaves or BotWristDeviceTarget.Amount or BotWristDeviceTarget.Confirm or BotWristDeviceTarget.Back) => true,
            (BotWristDeviceScreen.BuildHouse, BotWristDeviceTarget.Confirm or BotWristDeviceTarget.Back) => true,
            _ => false
        };
    }

    public Vector2 GetPanelPoint(int virtualX, int virtualY)
    {
        if (PanelRect.W <= 0 || PanelRect.H <= 0)
        {
            return Vector2.Zero;
        }

        var x = PanelRect.X + virtualX * PanelRect.W / (float)VirtualPanelWidthPixels;
        var y = PanelRect.Y + virtualY * PanelRect.H / (float)VirtualPanelHeightPixels;
        return new Vector2(x, y);
    }

    private void BuildElements()
    {
        AddElement(BotWristDeviceTarget.Gather, 24, 196, VirtualPanelWidthPixels - 48, 42, interactive: true);
        AddElement(BotWristDeviceTarget.BuildHouse, 24, 248, VirtualPanelWidthPixels - 48, 42, interactive: true);
        AddElement(BotWristDeviceTarget.Cancel, 24, 300, VirtualPanelWidthPixels - 48, 42, interactive: true);
        AddElement(BotWristDeviceTarget.Close, 24, 352, VirtualPanelWidthPixels - 48, 42, interactive: true);

        AddElement(BotWristDeviceTarget.Wood, 24, 226, 154, 42, interactive: true);
        AddElement(BotWristDeviceTarget.Stone, 190, 226, 154, 42, interactive: true);
        AddElement(BotWristDeviceTarget.Dirt, 24, 278, 154, 42, interactive: true);
        AddElement(BotWristDeviceTarget.Leaves, 190, 278, 154, 42, interactive: true);
        AddElement(BotWristDeviceTarget.Amount, 24, 330, VirtualPanelWidthPixels - 48, 56, interactive: false);
        AddElement(BotWristDeviceTarget.Confirm, 24, 394, VirtualPanelWidthPixels - 48, 42, interactive: true);
        AddElement(BotWristDeviceTarget.Back, 24, 438, VirtualPanelWidthPixels - 48, 34, interactive: true);
    }

    private void AddElement(BotWristDeviceTarget target, int x, int y, int width, int height, bool interactive)
    {
        var screenRect = ScaleRect(x, y, width, height);
        var worldCenter = ProjectVirtualCenter(x, y, width, height);
        var worldWidth = PanelWorldWidthUnits * width / VirtualPanelWidthPixels;
        var worldHeight = PanelWorldHeight * height / VirtualPanelHeightPixels;
        _elements[target] = new BotWristDeviceElement(target, screenRect, worldCenter, worldWidth, worldHeight, interactive);
    }

    private BotWristDeviceRect ScaleRect(int x, int y, int width, int height)
    {
        if (PanelRect.W <= 0 || PanelRect.H <= 0)
        {
            return new BotWristDeviceRect(0, 0, 0, 0);
        }

        var scaledX = (int)MathF.Round(PanelRect.X + x * PanelRect.W / (float)VirtualPanelWidthPixels);
        var scaledY = (int)MathF.Round(PanelRect.Y + y * PanelRect.H / (float)VirtualPanelHeightPixels);
        var scaledW = Math.Max(1, (int)MathF.Round(width * PanelRect.W / (float)VirtualPanelWidthPixels));
        var scaledH = Math.Max(1, (int)MathF.Round(height * PanelRect.H / (float)VirtualPanelHeightPixels));
        return new BotWristDeviceRect(scaledX, scaledY, scaledW, scaledH);
    }

    private Vector3 ProjectVirtualCenter(int x, int y, int width, int height)
    {
        var localX = ((x + width * 0.5f) / VirtualPanelWidthPixels - 0.5f) * PanelWorldWidthUnits;
        var localY = (0.5f - (y + height * 0.5f) / VirtualPanelHeightPixels) * PanelWorldHeight;
        return PanelCenter + PanelRight * localX + PanelUp * localY;
    }

    private static BotWristDeviceRect ProjectRect(
        Camera3D camera,
        int screenWidth,
        int screenHeight,
        Vector3 center,
        Vector3 right,
        Vector3 up,
        float width,
        float height)
    {
        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;
        Span<Vector3> corners =
        [
            center - right * halfWidth + up * halfHeight,
            center + right * halfWidth + up * halfHeight,
            center - right * halfWidth - up * halfHeight,
            center + right * halfWidth - up * halfHeight
        ];

        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;

        for (var i = 0; i < corners.Length; i++)
        {
            if (!TryProjectPoint(camera, screenWidth, screenHeight, corners[i], out var point))
            {
                return new BotWristDeviceRect(0, 0, 0, 0);
            }

            minX = MathF.Min(minX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxX = MathF.Max(maxX, point.X);
            maxY = MathF.Max(maxY, point.Y);
        }

        var x = (int)MathF.Floor(minX);
        var y = (int)MathF.Floor(minY);
        var w = Math.Max(1, (int)MathF.Ceiling(maxX - minX));
        var h = Math.Max(1, (int)MathF.Ceiling(maxY - minY));
        return new BotWristDeviceRect(x, y, w, h);
    }

    internal static bool TryProjectPoint(Camera3D camera, int screenWidth, int screenHeight, Vector3 worldPoint, out Vector2 screenPoint)
    {
        screenPoint = Vector2.Zero;
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return false;
        }

        var forwardRaw = camera.Target - camera.Position;
        if (forwardRaw.LengthSquared() <= 0.000001f)
        {
            return false;
        }

        var forward = Vector3.Normalize(forwardRaw);
        var rightRaw = Vector3.Cross(forward, camera.Up);
        var right = rightRaw.LengthSquared() <= 0.000001f
            ? Vector3.UnitX
            : Vector3.Normalize(rightRaw);
        var up = Vector3.Normalize(Vector3.Cross(right, forward));

        var relative = worldPoint - camera.Position;
        var cameraX = Vector3.Dot(relative, right);
        var cameraY = Vector3.Dot(relative, up);
        var cameraZ = Vector3.Dot(relative, forward);
        if (cameraZ <= 0.01f)
        {
            return false;
        }

        var aspect = screenWidth / (float)screenHeight;
        var halfHeight = MathF.Tan(camera.FovY * MathF.PI / 360f) * cameraZ;
        if (halfHeight <= 0.000001f)
        {
            return false;
        }

        var halfWidth = halfHeight * aspect;
        var ndcX = cameraX / halfWidth;
        var ndcY = cameraY / halfHeight;

        screenPoint = new Vector2(
            (ndcX * 0.5f + 0.5f) * screenWidth,
            (0.5f - ndcY * 0.5f) * screenHeight);
        return true;
    }
}
