using System.Numerics;
using AIG.Game.Config;
using AIG.Game.Gameplay;
using AIG.Game.Player;
using AIG.Game.World;

namespace AIG.Game.Bot;

internal sealed class CompanionBot
{
    private const float ReachDistance = 7.0f;
    private const float MoveArrivalDistance = 0.42f;
    private const float ActionArrivalDistance = 0.08f;
    private const float PreciseActionArrivalDistance = 0.01f;
    private const float ActionVerticalArrivalTolerance = 0.18f;
    private const float GatherTargetBlockDuration = 1.75f;
    private const float RouteProgressThreshold = 0.015f;
    private const float RouteMicroMoveThreshold = 0.05f;
    private const int BuildReachableLookahead = 256;
    private const int BuildReachableRouteProbeLimit = 12;

    private readonly record struct ResourceTarget(int X, int Y, int Z, BotResourceType Resource);
    private readonly record struct NavigationGoal(
        NavigationPurpose Purpose,
        int X,
        int Y,
        int Z,
        int Radius,
        HouseBlueprint? Blueprint);
    private readonly record struct ScoredBuildCandidate(int Index, HouseBuildStep Step, float Score, bool HasResources);

    private readonly GameConfig _config;
    private readonly Action<string> _diagnostics;
    private readonly Dictionary<BlockType, int> _stockpile = new();
    private readonly Dictionary<ResourceTarget, float> _blockedResourceTargets = new();

    private BotCommand? _activeCommand;
    private BotCommand? _queuedCommand;
    private ResourceTarget? _currentTarget;
    private ResourceTarget? _resourceFocusTarget;
    private int _activeGatheredAmount;
    private int _buildStepIndex;
    private BlockType? _buildGatherBlock;
    private int _buildGatherFromStepIndex;
    private float _actionCooldown;
    private float _retargetCooldown;
    private float _stuckTime;
    private int _strafeSign = 1;
    private Vector3 _lastPosition;
    private Vector3 _lastPlayerPosition;
    private bool _hasPlayerPosition;
    private float _noPathTimer;
    private Vector3[] _navigationWaypoints = Array.Empty<Vector3>();
    private int _navigationWaypointIndex;
    private NavigationGoal _navigationGoal;

    internal CompanionBot(GameConfig config, Vector3 spawnPosition, Action<string>? diagnostics = null)
    {
        _config = config;
        _diagnostics = diagnostics ?? (_ => { });
        Actor = new PlayerController(config, spawnPosition);
        Actor.SetPose(spawnPosition, new Vector3(0f, 0f, -1f));
        _lastPosition = spawnPosition;
        Trace($"spawn pos={FormatVector(spawnPosition)}");
    }

    internal PlayerController Actor { get; }
    internal Vector3 Position => Actor.Position;
    internal float Yaw => Actor.Yaw;
    internal BotStatus Status { get; private set; } = BotStatus.Idle;
    internal BotCommand? ActiveCommand => _activeCommand;
    internal BotCommand? QueuedCommand => _queuedCommand;
    internal int BuildStepIndex => _buildStepIndex;
    internal int GatheredAmount => _activeGatheredAmount;
    internal IReadOnlyDictionary<BlockType, int> Stockpile => _stockpile;

    internal int GetStockpile(BlockType block)
    {
        return _stockpile.TryGetValue(block, out var count) ? count : 0;
    }

    internal bool Enqueue(BotCommand command)
    {
        if (_activeCommand is null)
        {
            _activeCommand = command;
            ResetCommandRuntime();
            Trace($"enqueue slot=active command={FormatCommand(command)}");
            return true;
        }

        if (_queuedCommand is null)
        {
            _queuedCommand = command;
            Trace($"enqueue slot=queue command={FormatCommand(command)}");
            return true;
        }

        Trace($"enqueue-rejected command={FormatCommand(command)}");
        return false;
    }

    internal void CancelAll()
    {
        Trace($"cancel active={FormatCommand(_activeCommand)} queued={FormatCommand(_queuedCommand)}");
        _activeCommand = null;
        _queuedCommand = null;
        ResetCommandRuntime();
        Status = BotStatus.Idle;
    }

    internal void Update(WorldMap world, Vector3 playerPosition, Vector3 playerLookDirection, float deltaTime)
    {
        var previousStatus = Status;
        _lastPlayerPosition = playerPosition;
        _hasPlayerPosition = true;
        var safeDelta = Math.Clamp(deltaTime, 1f / 240f, 0.1f);
        _actionCooldown = MathF.Max(0f, _actionCooldown - safeDelta);
        _retargetCooldown = MathF.Max(0f, _retargetCooldown - safeDelta);
        _noPathTimer = MathF.Max(0f, _noPathTimer - safeDelta);
        UpdateBlockedResourceTargetCooldowns(safeDelta);

        if (_activeCommand is null)
        {
            UpdateFollowPlayer(world, playerPosition, playerLookDirection, safeDelta);
            LogStatusChange(previousStatus);
            return;
        }

        switch (_activeCommand.Value.Kind)
        {
            case BotCommandKind.GatherResource:
                UpdateGatherCommand(world, _activeCommand.Value.Resource, _activeCommand.Value.Amount, safeDelta);
                break;
            case BotCommandKind.BuildHouse:
                UpdateBuildCommand(world, safeDelta);
                break;
        }

        LogStatusChange(previousStatus);
    }

    internal string GetActiveSummary()
    {
        if (_activeCommand is null)
        {
            return "нет";
        }

        return _activeCommand.Value.Kind switch
        {
            BotCommandKind.GatherResource => $"Сбор {_activeCommand.Value.Resource.GetLabel()}: {_activeGatheredAmount}/{_activeCommand.Value.Amount}",
            BotCommandKind.BuildHouse => $"Стройка {_activeCommand.Value.Blueprint?.Name ?? "Дом"}: {_buildStepIndex}/{_activeCommand.Value.Blueprint?.Steps.Count ?? 0}",
            _ => "нет"
        };
    }

    internal string GetQueuedSummary()
    {
        if (_queuedCommand is null)
        {
            return "нет";
        }

        return _queuedCommand.Value.Kind switch
        {
            BotCommandKind.GatherResource => $"Сбор {_queuedCommand.Value.Resource.GetLabel()} {_queuedCommand.Value.Amount}",
            BotCommandKind.BuildHouse => _queuedCommand.Value.Blueprint?.Name ?? "Дом",
            _ => "нет"
        };
    }

    internal string GetStockpileSummary()
    {
        var wood = GetStockpile(BlockType.Wood);
        var stone = GetStockpile(BlockType.Stone);
        var dirt = GetStockpile(BlockType.Dirt);
        var leaves = GetStockpile(BlockType.Leaves);
        return $"дер:{wood} кам:{stone} зем:{dirt} лист:{leaves}";
    }

    private void UpdateFollowPlayer(WorldMap world, Vector3 playerPosition, Vector3 playerLookDirection, float deltaTime)
    {
        if (_noPathTimer > 0f)
        {
            Status = BotStatus.NoPath;
            StepIdle(world, deltaTime);
            return;
        }

        var followForward = ToHorizontal(playerLookDirection);
        var followRight = new Vector3(-followForward.Z, 0f, followForward.X);
        var desired = playerPosition - followForward * 2.2f + followRight * 1.4f;

        if (Vector3.DistanceSquared(Position, desired) < 9f)
        {
            ResetNavigationRoute();
            Status = BotStatus.Idle;
            StepIdle(world, deltaTime);
            return;
        }

        if (TryEnsureStandRoute(world, desired, goalRadius: 1))
        {
            Status = BotStatus.Moving;
            var moveResult = MoveAlongNavigationRoute(world, deltaTime, MoveArrivalDistance);
            if (moveResult == MoveResult.Blocked)
            {
                Status = BotStatus.NoPath;
                _noPathTimer = 0.35f;
            }

            if (moveResult == MoveResult.Arrived)
            {
                Status = BotStatus.Idle;
            }

            return;
        }

        ResetNavigationRoute();
        Status = BotStatus.Idle;
        StepIdle(world, deltaTime);
    }

    private void UpdateGatherCommand(WorldMap world, BotResourceType resource, int amount, float deltaTime)
    {
        if (_noPathTimer > 0f)
        {
            Status = BotStatus.NoPath;
            StepIdle(world, deltaTime);
            return;
        }

        if (_activeGatheredAmount >= amount)
        {
            CompleteActiveCommand();
            Update(world, Position, Actor.LookDirection, deltaTime);
            return;
        }

        Status = BotStatus.Gathering;
        _ = ExecuteGatherObjective(world, resource, amount - _activeGatheredAmount, deltaTime, countForActiveCommand: true);
    }

    private void UpdateBuildCommand(WorldMap world, float deltaTime)
    {
        if (_noPathTimer > 0f)
        {
            Status = BotStatus.NoPath;
            StepIdle(world, deltaTime);
            return;
        }

        var blueprint = _activeCommand?.Blueprint;
        if (blueprint is null)
        {
            CompleteActiveCommand();
            return;
        }

        while (_buildStepIndex < blueprint.Steps.Count && IsBuildStepSatisfied(world, blueprint.Steps[_buildStepIndex]))
        {
            _buildStepIndex++;
        }

        if (_buildStepIndex >= blueprint.Steps.Count)
        {
            CompleteActiveCommand();
            return;
        }

        var stepIndex = _buildStepIndex;
        var step = blueprint.Steps[stepIndex];
        if (TryContinueBuildGatherBatch(world, blueprint, stepIndex, deltaTime))
        {
            return;
        }

        if (step.ConsumesResource && GetStockpile(step.Block) <= 0)
        {
            BeginBuildGatherBatch(step.Block, stepIndex);
            BeginGatherForBuildStep(world, blueprint, stepIndex, step, deltaTime);
            return;
        }

        Status = BotStatus.Building;
        if (CanActOnBlock(step.X, step.Y, step.Z))
        {
            ResetNavigationRoute();
            TryApplyBuildStep(world, step);
            return;
        }

        if (!TryEnsureActionRoute(world, NavigationPurpose.BuildAction, step.X, step.Y, step.Z, searchRadius: 7, blueprint)
            && !TrySelectReachableBuildStep(world, blueprint, out stepIndex, out step))
        {
            Trace($"build-no-path step={FormatBuildStep(step)}");
            Status = BotStatus.NoPath;
            _noPathTimer = 0.35f;
            StepIdle(world, deltaTime);
            return;
        }

        if (step.ConsumesResource && GetStockpile(step.Block) <= 0)
        {
            BeginBuildGatherBatch(step.Block, stepIndex);
            BeginGatherForBuildStep(world, blueprint, stepIndex, step, deltaTime);
            return;
        }

        if (CanActOnBlock(step.X, step.Y, step.Z))
        {
            ResetNavigationRoute();
            TryApplyBuildStep(world, step);
            return;
        }

        var buildMove = MoveAlongNavigationRoute(world, deltaTime, ActionArrivalDistance);
        if (buildMove == MoveResult.Arrived && !CanActOnBlock(step.X, step.Y, step.Z))
        {
            ResetNavigationRoute();
            Trace($"build-arrived-out-of-range step={FormatBuildStep(step)}");
            if (!TrySelectReachableBuildStep(world, blueprint, out stepIndex, out step))
            {
                Status = BotStatus.NoPath;
                _noPathTimer = 0.35f;
                StepIdle(world, deltaTime);
                return;
            }

            if (step.ConsumesResource && GetStockpile(step.Block) <= 0)
            {
                BeginBuildGatherBatch(step.Block, stepIndex);
                BeginGatherForBuildStep(world, blueprint, stepIndex, step, deltaTime);
                return;
            }

            if (CanActOnBlock(step.X, step.Y, step.Z))
            {
                ResetNavigationRoute();
                TryApplyBuildStep(world, step);
                return;
            }

            buildMove = MoveAlongNavigationRoute(world, deltaTime, ActionArrivalDistance);
        }

        if (buildMove == MoveResult.Blocked)
        {
            Trace($"build-move-blocked step={FormatBuildStep(step)}");
            Status = BotStatus.NoPath;
            _noPathTimer = 0.35f;
        }
    }

    private void BeginGatherForBuildStep(WorldMap world, HouseBlueprint blueprint, int stepIndex, HouseBuildStep step, float deltaTime)
    {
        if (!TryGetBuildResourceNeed(blueprint, stepIndex, step, out var resource, out var remainingNeeded))
        {
            return;
        }

        Status = BotStatus.Gathering;
        _ = ExecuteGatherObjective(world, resource, remainingNeeded, deltaTime, countForActiveCommand: false);
    }

    private void BeginBuildGatherBatch(BlockType block, int stepIndex)
    {
        _buildGatherBlock = block;
        _buildGatherFromStepIndex = stepIndex;
    }

    private bool TryContinueBuildGatherBatch(WorldMap world, HouseBlueprint blueprint, int stepIndex, float deltaTime)
    {
        if (_buildGatherBlock is not BlockType block)
        {
            return false;
        }

        var fromIndex = Math.Max(_buildGatherFromStepIndex, stepIndex);
        var remainingNeeded = Math.Max(0, blueprint.CountRemaining(block, fromIndex) - GetStockpile(block));
        if (remainingNeeded <= 0)
        {
            ClearBuildGatherBatch();
            return false;
        }

        Status = BotStatus.Gathering;
        _ = ExecuteGatherObjective(world, BotResourceTypeExtensions.FromBlock(block), remainingNeeded, deltaTime, countForActiveCommand: false);
        return true;
    }

    private void ClearBuildGatherBatch()
    {
        _buildGatherBlock = null;
        _buildGatherFromStepIndex = 0;
    }

    private bool TryGetBuildResourceNeed(
        HouseBlueprint blueprint,
        int stepIndex,
        HouseBuildStep step,
        out BotResourceType resource,
        out int remainingNeeded)
    {
        resource = default;
        remainingNeeded = 0;
        if (!step.ConsumesResource)
        {
            return false;
        }

        remainingNeeded = Math.Max(0, blueprint.CountRemaining(step.Block, stepIndex) - GetStockpile(step.Block));
        if (remainingNeeded <= 0)
        {
            return false;
        }

        resource = BotResourceTypeExtensions.FromBlock(step.Block);
        return true;
    }

    private bool ExecuteGatherObjective(WorldMap world, BotResourceType resource, int amountNeeded, float deltaTime, bool countForActiveCommand)
    {
        if (amountNeeded <= 0)
        {
            return true;
        }

        if (!TryAcquireResourceTarget(world, resource, out var target))
        {
            Trace($"gather-target-missing resource={resource} needed={amountNeeded}");
            Status = BotStatus.NoPath;
            StepIdle(world, deltaTime);
            return false;
        }

        if (CanActOnBlock(target.X, target.Y, target.Z))
        {
            ResetNavigationRoute();
            return TryHarvestTarget(world, target, countForActiveCommand);
        }

        var reservedBlueprint = _activeCommand is BotCommand { Kind: BotCommandKind.BuildHouse, Blueprint: not null } active
            ? active.Blueprint
            : null;
        if (!TryEnsureActionRoute(world, NavigationPurpose.GatherAction, target.X, target.Y, target.Z, searchRadius: 6, reservedBlueprint))
        {
            BlockResourceTarget(target, GatherTargetBlockDuration);
            _currentTarget = null;
            _retargetCooldown = 0.15f;
            Trace($"gather-route-failed target={FormatCell(target.X, target.Y, target.Z)} resource={resource}");
            Status = BotStatus.NoPath;
            StepIdle(world, deltaTime);
            return false;
        }

        var gatherMove = MoveAlongNavigationRoute(world, deltaTime, ActionArrivalDistance);
        if (gatherMove == MoveResult.Arrived && !CanActOnBlock(target.X, target.Y, target.Z))
        {
            BlockResourceTarget(target, GatherTargetBlockDuration);
            ResetNavigationRoute();
            _currentTarget = null;
            _retargetCooldown = 0.1f;
            Trace($"gather-arrived-out-of-range target={FormatCell(target.X, target.Y, target.Z)} resource={resource}");
            return false;
        }

        if (gatherMove == MoveResult.Blocked)
        {
            BlockResourceTarget(target, GatherTargetBlockDuration);
            _currentTarget = null;
            _retargetCooldown = 0.25f;
            Trace($"gather-move-blocked target={FormatCell(target.X, target.Y, target.Z)} resource={resource}");
            Status = BotStatus.NoPath;
            _noPathTimer = 0.35f;
        }

        return false;
    }

    private bool TryAcquireResourceTarget(WorldMap world, BotResourceType resource, out ResourceTarget target)
    {
        if (_currentTarget is ResourceTarget existing
            && existing.Resource == resource
            && !IsResourceTargetBlocked(existing)
            && IsResourceTargetValid(world, existing))
        {
            target = existing;
            return true;
        }

        if (_retargetCooldown > 0f)
        {
            target = default;
            return false;
        }

        if (TryFindNearestResource(world, resource, out target))
        {
            _currentTarget = target;
            _retargetCooldown = 0.12f;
            Trace($"target-acquired resource={resource} target={FormatCell(target.X, target.Y, target.Z)}");
            return true;
        }

        _currentTarget = null;
        _retargetCooldown = 0.25f;
        Trace($"target-search-empty resource={resource}");
        return false;
    }

    private bool TryFindNearestResource(WorldMap world, BotResourceType resource, out ResourceTarget target)
    {
        target = default;
        var reservedBlueprint = _activeCommand is BotCommand { Kind: BotCommandKind.BuildHouse, Blueprint: not null } active
            ? active.Blueprint
            : null;
        if (TryFindFocusedResourceTarget(world, resource, reservedBlueprint, out target))
        {
            return true;
        }

        var centerX = Math.Clamp((int)MathF.Floor(Position.X), 0, Math.Max(0, world.Width - 1));
        var centerZ = Math.Clamp((int)MathF.Floor(Position.Z), 0, Math.Max(0, world.Depth - 1));
        var chunkRadius = 2;
        var centerChunkX = centerX / Math.Max(1, world.ChunkSize);
        var centerChunkZ = centerZ / Math.Max(1, world.ChunkSize);
        var bestScore = float.MaxValue;
        var found = false;

        for (var chunkX = Math.Max(0, centerChunkX - chunkRadius); chunkX <= Math.Min(world.ChunkCountX - 1, centerChunkX + chunkRadius); chunkX++)
        {
            for (var chunkZ = Math.Max(0, centerChunkZ - chunkRadius); chunkZ <= Math.Min(world.ChunkCountZ - 1, centerChunkZ + chunkRadius); chunkZ++)
            {
                if (!world.TryGetChunkSurfaceBlocks(chunkX, chunkZ, out var surfaceBlocks) || surfaceBlocks.Count == 0)
                {
                    continue;
                }

                for (var i = 0; i < surfaceBlocks.Count; i++)
                {
                    var surface = surfaceBlocks[i];
                    if (!resource.Matches(surface.Block))
                    {
                        continue;
                    }

                    var candidate = new ResourceTarget(surface.X, surface.Y, surface.Z, resource);
                    if (IsResourceTargetBlocked(candidate))
                    {
                        continue;
                    }

                    if (reservedBlueprint?.IsPlannedSolidBlock(surface.X, surface.Y, surface.Z, surface.Block) == true)
                    {
                        continue;
                    }

                    if (IsProtectedResourceTarget(surface.X, surface.Y, surface.Z, reservedBlueprint))
                    {
                        continue;
                    }

                    var dx = surface.X + 0.5f - Position.X;
                    var dz = surface.Z + 0.5f - Position.Z;
                    var dy = surface.Y + 0.5f - Position.Y;
                    var horizontal = dx * dx + dz * dz;
                    var score = horizontal + MathF.Abs(dy) * 3.5f;
                    if (score >= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    target = candidate;
                    found = true;
                }
            }
        }

        return found;
    }

    private bool TryFindFocusedResourceTarget(WorldMap world, BotResourceType resource, HouseBlueprint? reservedBlueprint, out ResourceTarget target)
    {
        target = default;
        if (_resourceFocusTarget is not ResourceTarget focus || focus.Resource != resource)
        {
            return false;
        }

        var horizontalRadius = GetFocusedResourceHorizontalRadius(resource);
        var verticalRadius = GetFocusedResourceVerticalRadius(resource);
        var bestScore = float.MaxValue;
        var found = false;

        for (var x = Math.Max(0, focus.X - horizontalRadius); x <= Math.Min(world.Width - 1, focus.X + horizontalRadius); x++)
        {
            for (var y = Math.Max(0, focus.Y - verticalRadius); y <= Math.Min(world.Height - 1, focus.Y + verticalRadius); y++)
            {
                for (var z = Math.Max(0, focus.Z - horizontalRadius); z <= Math.Min(world.Depth - 1, focus.Z + horizontalRadius); z++)
                {
                    if (!resource.Matches(world.GetBlock(x, y, z)))
                    {
                        continue;
                    }

                    var candidate = new ResourceTarget(x, y, z, resource);
                    if (IsResourceTargetBlocked(candidate))
                    {
                        continue;
                    }

                    if (reservedBlueprint?.IsPlannedSolidBlock(x, y, z, world.GetBlock(x, y, z)) == true)
                    {
                        continue;
                    }

                    if (IsProtectedResourceTarget(x, y, z, reservedBlueprint))
                    {
                        continue;
                    }

                    var focusDx = x - focus.X;
                    var focusDy = y - focus.Y;
                    var focusDz = z - focus.Z;
                    var botDx = x + 0.5f - Position.X;
                    var botDy = y + 0.5f - Position.Y;
                    var botDz = z + 0.5f - Position.Z;
                    var score = (focusDx * focusDx + focusDz * focusDz) * 4f
                        + MathF.Abs(focusDy)
                        + (botDx * botDx + botDz * botDz) * 0.05f
                        + MathF.Abs(botDy) * 0.15f;
                    if (score >= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    target = candidate;
                    found = true;
                }
            }
        }

        if (found)
        {
            return true;
        }

        _resourceFocusTarget = null;
        return false;
    }

    private bool IsResourceTargetValid(WorldMap world, ResourceTarget target)
    {
        return world.IsInside(target.X, target.Y, target.Z)
            && target.Resource.Matches(world.GetBlock(target.X, target.Y, target.Z));
    }

    private void BlockResourceTarget(ResourceTarget target, float duration)
    {
        if (duration <= 0f)
        {
            return;
        }

        if (_blockedResourceTargets.TryGetValue(target, out var existingDuration) && existingDuration >= duration)
        {
            return;
        }

        _blockedResourceTargets[target] = duration;
    }

    private bool IsResourceTargetBlocked(ResourceTarget target)
    {
        return _blockedResourceTargets.TryGetValue(target, out var remaining) && remaining > 0f;
    }

    private void UpdateBlockedResourceTargetCooldowns(float deltaTime)
    {
        if (_blockedResourceTargets.Count == 0 || deltaTime <= 0f)
        {
            return;
        }

        List<(ResourceTarget Target, float Remaining)>? updatedTargets = null;
        List<ResourceTarget>? expiredTargets = null;
        foreach (var entry in _blockedResourceTargets)
        {
            var remaining = entry.Value - deltaTime;
            if (remaining > 0f)
            {
                updatedTargets ??= [];
                updatedTargets.Add((entry.Key, remaining));
                continue;
            }

            expiredTargets ??= [];
            expiredTargets.Add(entry.Key);
        }

        if (updatedTargets is not null)
        {
            for (var i = 0; i < updatedTargets.Count; i++)
            {
                var updated = updatedTargets[i];
                _blockedResourceTargets[updated.Target] = updated.Remaining;
            }
        }

        if (expiredTargets is null)
        {
            return;
        }

        for (var i = 0; i < expiredTargets.Count; i++)
        {
            _blockedResourceTargets.Remove(expiredTargets[i]);
        }
    }

    private bool TryHarvestTarget(WorldMap world, ResourceTarget target, bool countForActiveCommand)
    {
        if (_actionCooldown > 0f)
        {
            return false;
        }

        var currentBlock = world.GetBlock(target.X, target.Y, target.Z);
        if (!target.Resource.Matches(currentBlock))
        {
            _currentTarget = null;
            Trace($"target-stale resource={target.Resource} target={FormatCell(target.X, target.Y, target.Z)} actual={currentBlock}");
            return false;
        }

        var collectedBlock = target.Resource.ToBlockType();
        var collected = HarvestResourceCluster(world, target);
        if (collected <= 0)
        {
            Trace($"harvest-empty resource={target.Resource} target={FormatCell(target.X, target.Y, target.Z)}");
            return false;
        }

        if (countForActiveCommand)
        {
            _activeGatheredAmount += collected;
        }

        _currentTarget = null;
        _resourceFocusTarget = target;
        _actionCooldown = 0.08f;
        Trace($"harvest resource={target.Resource} target={FormatCell(target.X, target.Y, target.Z)} amount={collected} stock={GetStockpile(collectedBlock)}");
        return true;
    }

    private bool TryApplyBuildStep(WorldMap world, HouseBuildStep step)
    {
        if (_actionCooldown > 0f)
        {
            return false;
        }

        var current = world.GetBlock(step.X, step.Y, step.Z);
        if (current == step.Block)
        {
            return true;
        }

        if (step.ConsumesResource && !TryConsumeStockpile(step.Block, 1))
        {
            return false;
        }

        world.SetBlock(step.X, step.Y, step.Z, step.Block);
        _actionCooldown = 0.05f;
        Trace($"build-place step={FormatBuildStep(step)} stock={GetStockpile(step.Block)}");
        return true;
    }

    private bool IsBuildStepSatisfied(WorldMap world, HouseBuildStep step)
    {
        return world.GetBlock(step.X, step.Y, step.Z) == step.Block;
    }

    private void CompleteActiveCommand()
    {
        var completed = _activeCommand;
        _activeCommand = null;
        _currentTarget = null;
        ResetNavigationRoute();
        _activeGatheredAmount = 0;
        _buildStepIndex = 0;
        Trace($"command-complete command={FormatCommand(completed)}");
        if (_queuedCommand is BotCommand queued)
        {
            _activeCommand = queued;
            _queuedCommand = null;
            ResetCommandRuntime();
            Trace($"command-activate-from-queue command={FormatCommand(queued)}");
            return;
        }

        Status = BotStatus.Idle;
    }

    private void ResetCommandRuntime()
    {
        _currentTarget = null;
        _resourceFocusTarget = null;
        ClearBuildGatherBatch();
        _blockedResourceTargets.Clear();
        ResetNavigationRoute();
        _activeGatheredAmount = 0;
        _buildStepIndex = 0;
        _actionCooldown = 0f;
        _retargetCooldown = 0f;
        _stuckTime = 0f;
        _noPathTimer = 0f;
    }

    private void AddStockpile(BlockType block, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        _stockpile[block] = GetStockpile(block) + amount;
    }

    private bool TryConsumeStockpile(BlockType block, int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        var available = GetStockpile(block);
        if (available < amount)
        {
            return false;
        }

        _stockpile[block] = available - amount;
        return true;
    }

    private int HarvestResourceCluster(WorldMap world, ResourceTarget target)
    {
        var collectedBlock = target.Resource.ToBlockType();
        var reservedBlueprint = _activeCommand is BotCommand { Kind: BotCommandKind.BuildHouse, Blueprint: not null } active
            ? active.Blueprint
            : null;
        var harvested = 0;
        var maxHarvest = GetHarvestBatchSize(target.Resource);

        for (var distance = 0; distance <= 1 && harvested < maxHarvest; distance++)
        {
            for (var dx = -distance; dx <= distance && harvested < maxHarvest; dx++)
            {
                for (var dy = -distance; dy <= distance && harvested < maxHarvest; dy++)
                {
                    for (var dz = -distance; dz <= distance && harvested < maxHarvest; dz++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) != distance)
                        {
                            continue;
                        }

                        var x = target.X + dx;
                        var y = target.Y + dy;
                        var z = target.Z + dz;
                        if (!world.IsInside(x, y, z)
                            || IsProtectedResourceTarget(x, y, z, reservedBlueprint)
                            || !CanActOnBlock(x, y, z)
                            || !target.Resource.Matches(world.GetBlock(x, y, z)))
                        {
                            continue;
                        }

                        world.SetBlock(x, y, z, BlockType.Air);
                        harvested++;
                    }
                }
            }
        }

        AddStockpile(collectedBlock, harvested);
        return harvested;
    }

    private bool IsProtectedResourceTarget(int x, int y, int z, HouseBlueprint? reservedBlueprint)
    {
        if (IsSupportingPoseBlock(x, y, z, Position))
        {
            return true;
        }

        if (_hasPlayerPosition && IsSupportingPoseBlock(x, y, z, _lastPlayerPosition))
        {
            return true;
        }

        return reservedBlueprint?.IsInsideGatherKeepout(x, z) == true;
    }

    private bool IsSupportingPoseBlock(int x, int y, int z, Vector3 pose)
    {
        var supportY = (int)MathF.Floor(pose.Y - 0.05f);
        if (y != supportY)
        {
            return false;
        }

        var minX = (int)MathF.Floor(pose.X - Actor.ColliderHalfWidth + 0.03f);
        var maxX = (int)MathF.Floor(pose.X + Actor.ColliderHalfWidth - 0.03f);
        var minZ = (int)MathF.Floor(pose.Z - Actor.ColliderHalfWidth + 0.03f);
        var maxZ = (int)MathF.Floor(pose.Z + Actor.ColliderHalfWidth - 0.03f);
        return x >= minX && x <= maxX && z >= minZ && z <= maxZ;
    }

    private static int GetHarvestBatchSize(BotResourceType resource)
    {
        return resource switch
        {
            BotResourceType.Wood => 6,
            BotResourceType.Leaves => 8,
            BotResourceType.Dirt => 4,
            BotResourceType.Stone => 4,
            _ => 1
        };
    }

    private static int GetFocusedResourceHorizontalRadius(BotResourceType resource)
    {
        return resource switch
        {
            BotResourceType.Wood => 2,
            BotResourceType.Leaves => 2,
            _ => 1
        };
    }

    private static int GetFocusedResourceVerticalRadius(BotResourceType resource)
    {
        return resource switch
        {
            BotResourceType.Wood => 6,
            BotResourceType.Leaves => 6,
            _ => 2
        };
    }

    private MoveResult MoveTowardsPose(WorldMap world, Vector3 targetPose, float deltaTime)
    {
        return MoveTowardsPose(world, targetPose, deltaTime, MoveArrivalDistance);
    }

    private MoveResult MoveTowardsPose(
        WorldMap world,
        Vector3 targetPose,
        float deltaTime,
        float arrivalDistance,
        float verticalArrivalTolerance = 0.95f)
    {
        var delta = targetPose - Position;
        var horizontalDelta = new Vector2(delta.X, delta.Z);
        var horizontalDistance = horizontalDelta.Length();
        var distanceBefore = delta.Length();
        if (horizontalDistance <= arrivalDistance && MathF.Abs(delta.Y) <= verticalArrivalTolerance)
        {
            StepIdle(world, deltaTime);
            _stuckTime = 0f;
            return MoveResult.Arrived;
        }

        var desiredForward = horizontalDistance <= 0.0001f
            ? ToHorizontal(Actor.LookDirection)
            : Vector3.Normalize(new Vector3(horizontalDelta.X, 0f, horizontalDelta.Y));
        var currentForward = new Vector3(MathF.Sin(Actor.Yaw), 0f, MathF.Cos(Actor.Yaw));
        var currentRight = new Vector3(-currentForward.Z, 0f, currentForward.X);
        var moveForward = Math.Clamp(Vector3.Dot(desiredForward, currentForward), -1f, 1f);
        var moveRight = Math.Clamp(Vector3.Dot(desiredForward, currentRight), -1f, 1f);

        if (_stuckTime > 0.55f)
        {
            moveRight = Math.Clamp(moveRight + _strafeSign * 0.85f, -1f, 1f);
        }

        var desiredYaw = MathF.Atan2(desiredForward.X, desiredForward.Z);
        var yawDelta = NormalizeAngle(desiredYaw - Actor.Yaw);
        var sensitivity = MathF.Abs(_config.MouseSensitivity) < 0.00001f ? 0.0025f : _config.MouseSensitivity;
        var lookDeltaX = Math.Clamp(-yawDelta / sensitivity, -240f, 240f);

        var aheadEye = Actor.EyePosition - new Vector3(0f, 0.7f, 0f);
        var obstacleAhead = VoxelRaycaster.Raycast(world, aheadEye, desiredForward, 0.95f) is not null;
        var aheadPoint = Position + desiredForward * 0.85f;
        var hasGroundAhead = world.IsSolidAt(new Vector3(aheadPoint.X, Position.Y - 0.18f, aheadPoint.Z));
        var jump = Actor.IsGrounded && (obstacleAhead || delta.Y > 0.8f || !hasGroundAhead);

        var before = Position;
        Actor.Update(world, new PlayerInput(moveForward, moveRight, jump, lookDeltaX, 0f), deltaTime);

        var moved = new Vector2(Position.X - before.X, Position.Z - before.Z).Length();
        var distanceAfter = Vector3.Distance(Position, targetPose);
        UpdateRouteProgress(deltaTime, moved, distanceBefore, distanceAfter);

        _lastPosition = Position;
        if (_stuckTime > 0.75f)
        {
            _strafeSign = -_strafeSign;
        }

        return _stuckTime > 2.2f ? MoveResult.Blocked : MoveResult.Moving;
    }

    private void UpdateRouteProgress(float deltaTime, float movedDistance, float distanceBefore, float distanceAfter)
    {
        if (distanceBefore <= 0.55f)
        {
            _stuckTime = MathF.Max(0f, _stuckTime - deltaTime * 2f);
            return;
        }

        var progress = distanceBefore - distanceAfter;
        if (progress >= RouteProgressThreshold)
        {
            _stuckTime = MathF.Max(0f, _stuckTime - deltaTime * 1.5f);
            return;
        }

        _stuckTime += movedDistance <= RouteMicroMoveThreshold
            ? deltaTime
            : deltaTime * 0.65f;
    }

    private bool TryEnsureStandRoute(WorldMap world, Vector3 desiredPose, int goalRadius)
    {
        var desiredX = Math.Clamp((int)MathF.Floor(desiredPose.X), 0, Math.Max(0, world.Width - 1));
        var desiredFeetCell = Math.Clamp((int)MathF.Floor(desiredPose.Y + 0.1f), 1, Math.Max(1, world.Height - 2));
        var desiredZ = Math.Clamp((int)MathF.Floor(desiredPose.Z), 0, Math.Max(0, world.Depth - 1));
        var goal = new NavigationGoal(NavigationPurpose.Follow, desiredX, desiredFeetCell, desiredZ, goalRadius, null);
        if (goal == _navigationGoal)
        {
            return true;
        }

        if (!BotNavigator.TryBuildStandRoute(world, GetNavigationSettings(), Position, desiredPose, goalRadius, out var waypoints))
        {
            ResetNavigationRoute();
            Trace($"follow-route-failed desired={FormatVector(desiredPose)} radius={goalRadius}");
            return false;
        }

        _navigationGoal = goal;
        _navigationWaypoints = waypoints;
        _navigationWaypointIndex = 0;
        Trace($"follow-route waypoints={waypoints.Length} desired={FormatVector(desiredPose)} radius={goalRadius}");
        return true;
    }

    private bool TryEnsureActionRoute(
        WorldMap world,
        NavigationPurpose purpose,
        int targetX,
        int targetY,
        int targetZ,
        int searchRadius,
        HouseBlueprint? blueprint)
    {
        var goal = new NavigationGoal(purpose, targetX, targetY, targetZ, searchRadius, blueprint);
        if (goal == _navigationGoal)
        {
            if (CanActOnBlock(targetX, targetY, targetZ))
            {
                return true;
            }

            if (_navigationWaypointIndex < _navigationWaypoints.Length)
            {
                return true;
            }
        }

        if (TryFindActionPoseNear(world, targetX, targetY, targetZ, searchRadius, blueprint, out var localPose, out _))
        {
            var localDistanceSq = Vector3.DistanceSquared(Position, localPose);
            if (localDistanceSq <= 36f)
            {
                if (!HasArrivedAtPose(localPose, ActionArrivalDistance, ActionVerticalArrivalTolerance))
                {
                    ApplyNavigationRoute(goal, [localPose]);
                    Trace($"action-route-local purpose={purpose} target={FormatCell(targetX, targetY, targetZ)} waypoints=1 pose={FormatVector(localPose)}");
                    return true;
                }
            }
            else if (BotNavigator.TryBuildStandRoute(world, GetNavigationSettings(), Position, localPose, goalRadius: 1, out var stageWaypoints))
            {
                ApplyNavigationRoute(goal, stageWaypoints);
                Trace($"action-route-stage purpose={purpose} target={FormatCell(targetX, targetY, targetZ)} waypoints={stageWaypoints.Length} pose={FormatVector(localPose)}");
                return true;
            }
        }

        if (!BotNavigator.TryBuildActionRoute(
                world,
                GetNavigationSettings(),
                Position,
                targetX,
                targetY,
                targetZ,
                searchRadius,
                blueprint,
                out var waypoints,
                out _))
        {
            ResetNavigationRoute();
            Trace($"action-route-failed purpose={purpose} target={FormatCell(targetX, targetY, targetZ)} radius={searchRadius}");
            return false;
        }

        ApplyNavigationRoute(goal, waypoints);
        Trace($"action-route purpose={purpose} target={FormatCell(targetX, targetY, targetZ)} waypoints={waypoints.Length} radius={searchRadius}");
        return true;
    }

    private bool TrySelectReachableBuildStep(WorldMap world, HouseBlueprint blueprint, out int stepIndex, out HouseBuildStep step)
    {
        stepIndex = _buildStepIndex;
        step = blueprint.Steps[_buildStepIndex];
        var settings = GetNavigationSettings();
        var lookaheadEnd = Math.Min(blueprint.Steps.Count, _buildStepIndex + BuildReachableLookahead);
        var routeCandidates = new List<ScoredBuildCandidate>(Math.Min(BuildReachableRouteProbeLimit * 2, lookaheadEnd - _buildStepIndex));

        var bestIndex = -1;
        var bestStep = default(HouseBuildStep);
        var bestWaypoints = Array.Empty<Vector3>();
        var bestGoal = default(NavigationGoal);
        var bestScore = float.MaxValue;

        for (var candidateIndex = _buildStepIndex + 1; candidateIndex < lookaheadEnd; candidateIndex++)
        {
            var candidate = blueprint.Steps[candidateIndex];
            if (IsBuildStepSatisfied(world, candidate))
            {
                continue;
            }

            var hasResources = !candidate.ConsumesResource || GetStockpile(candidate.Block) > 0;
            if (CanActOnBlock(candidate.X, candidate.Y, candidate.Z))
            {
                if (!hasResources)
                {
                    continue;
                }

                ResetNavigationRoute();
                stepIndex = candidateIndex;
                step = candidate;
                Trace($"build-step-direct index={candidateIndex} step={FormatBuildStep(candidate)}");
                return true;
            }

            routeCandidates.Add(new ScoredBuildCandidate(candidateIndex, candidate, ScoreBuildRerouteCandidate(candidateIndex, candidate, hasResources), hasResources));
        }

        foreach (var routeCandidate in routeCandidates
                     .OrderBy(candidate => candidate.Score)
                     .Take(BuildReachableRouteProbeLimit))
        {
            var candidateIndex = routeCandidate.Index;
            var candidate = routeCandidate.Step;
            var hasResources = routeCandidate.HasResources;
            if (!BotNavigator.TryBuildActionRoute(
                    world,
                    settings,
                    Position,
                    candidate.X,
                    candidate.Y,
                    candidate.Z,
                    searchRadius: 7,
                    blueprint,
                    out var waypoints,
                    out _))
            {
                continue;
            }

            var routeScore = waypoints.Length + routeCandidate.Score;

            if (routeScore >= bestScore)
            {
                continue;
            }

            bestScore = routeScore;
            bestIndex = candidateIndex;
            bestStep = candidate;
            bestWaypoints = waypoints;
            bestGoal = new NavigationGoal(NavigationPurpose.BuildAction, candidate.X, candidate.Y, candidate.Z, 7, blueprint);
        }

        if (bestIndex < 0)
        {
            return false;
        }

        ApplyNavigationRoute(bestGoal, bestWaypoints);
        stepIndex = bestIndex;
        step = bestStep;
        Trace($"build-step-reroute index={bestIndex} step={FormatBuildStep(bestStep)} waypoints={bestWaypoints.Length}");
        return true;
    }

    private float ScoreBuildRerouteCandidate(int candidateIndex, HouseBuildStep candidate, bool hasResources)
    {
        var center = new Vector3(candidate.X + 0.5f, candidate.Y + 0.5f, candidate.Z + 0.5f);
        var distancePenalty = Vector3.DistanceSquared(Position, center) * 0.04f;
        var orderPenalty = (candidateIndex - _buildStepIndex) * 0.35f;
        var resourcePenalty = hasResources ? 0f : 50f;
        return distancePenalty + orderPenalty + resourcePenalty;
    }

    private MoveResult MoveAlongNavigationRoute(WorldMap world, float deltaTime, float arrivalDistance)
    {
        while (_navigationWaypointIndex < _navigationWaypoints.Length
               && HasArrivedAtPose(
                   _navigationWaypoints[_navigationWaypointIndex],
                   GetNavigationArrivalDistance(arrivalDistance, _navigationWaypointIndex),
                   GetNavigationVerticalArrivalTolerance(_navigationWaypointIndex)))
        {
            _navigationWaypointIndex++;
        }

        if (_navigationWaypointIndex >= _navigationWaypoints.Length)
        {
            StepIdle(world, deltaTime);
            _stuckTime = 0f;
            return MoveResult.Arrived;
        }

        var result = MoveTowardsPose(
            world,
            _navigationWaypoints[_navigationWaypointIndex],
            deltaTime,
            GetNavigationArrivalDistance(arrivalDistance, _navigationWaypointIndex),
            GetNavigationVerticalArrivalTolerance(_navigationWaypointIndex));
        if (result == MoveResult.Blocked)
        {
            Trace($"route-blocked goal={_navigationGoal.Purpose} waypointIndex={_navigationWaypointIndex} waypoint={FormatVector(_navigationWaypoints[_navigationWaypointIndex])}");
            ResetNavigationRoute();
        }

        return result;
    }

    private float GetNavigationArrivalDistance(float finalArrivalDistance, int waypointIndex)
    {
        if (waypointIndex < _navigationWaypoints.Length - 1)
        {
            return MoveArrivalDistance;
        }

        return _navigationGoal.Purpose == NavigationPurpose.Follow
            ? finalArrivalDistance
            : MathF.Min(finalArrivalDistance, PreciseActionArrivalDistance);
    }

    private float GetNavigationVerticalArrivalTolerance(int waypointIndex)
    {
        if (waypointIndex < _navigationWaypoints.Length - 1 || _navigationGoal.Purpose == NavigationPurpose.Follow)
        {
            return 0.95f;
        }

        return ActionVerticalArrivalTolerance;
    }

    private bool HasArrivedAtPose(Vector3 targetPose, float arrivalDistance, float verticalArrivalTolerance = 0.95f)
    {
        var delta = targetPose - Position;
        var horizontalDistance = new Vector2(delta.X, delta.Z).Length();
        return horizontalDistance <= arrivalDistance && MathF.Abs(delta.Y) <= verticalArrivalTolerance;
    }

    private void ResetNavigationRoute()
    {
        _navigationWaypoints = Array.Empty<Vector3>();
        _navigationWaypointIndex = 0;
        _navigationGoal = default;
    }

    private void ApplyNavigationRoute(NavigationGoal goal, Vector3[] waypoints)
    {
        _navigationGoal = goal;
        _navigationWaypoints = waypoints;
        _navigationWaypointIndex = 0;
    }

    private BotNavigationSettings GetNavigationSettings()
    {
        return new BotNavigationSettings(Actor.ColliderHalfWidth, Actor.ColliderHeight, ReachDistance);
    }

    private void StepIdle(WorldMap world, float deltaTime)
    {
        Actor.Update(world, new PlayerInput(0f, 0f, false, 0f, 0f), deltaTime);
        _lastPosition = Position;
    }

    private void LogStatusChange(BotStatus previousStatus)
    {
        if (previousStatus == Status)
        {
            return;
        }

        Trace($"status {previousStatus}->{Status} pos={FormatVector(Position)} active={FormatCommand(_activeCommand)}");
    }

    private void Trace(string message)
    {
        _diagnostics(message);
    }

    private static string FormatCommand(BotCommand? command)
    {
        if (command is not BotCommand value)
        {
            return "none";
        }

        return value.Kind switch
        {
            BotCommandKind.GatherResource => $"gather:{value.Resource}:{value.Amount}",
            BotCommandKind.BuildHouse => $"build:{value.Blueprint?.Name ?? "house"}",
            _ => value.Kind.ToString()
        };
    }

    private static string FormatBuildStep(HouseBuildStep step)
    {
        return $"{step.Block}@{FormatCell(step.X, step.Y, step.Z)} consume={step.ConsumesResource}";
    }

    private static string FormatCell(int x, int y, int z)
    {
        return $"{x},{y},{z}";
    }

    private static string FormatVector(Vector3 value)
    {
        return $"{value.X:0.00},{value.Y:0.00},{value.Z:0.00}";
    }

    private bool CanActOnBlock(int x, int y, int z)
    {
        var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
        var dx = center.X - Actor.EyePosition.X;
        var dy = center.Y - Actor.EyePosition.Y;
        var dz = center.Z - Actor.EyePosition.Z;
        var distanceSq = dx * dx + dy * dy + dz * dz;
        return distanceSq <= ReachDistance * ReachDistance;
    }

    private bool TryFindActionPoseNear(
        WorldMap world,
        int targetX,
        int targetY,
        int targetZ,
        int searchRadius,
        HouseBlueprint? blueprint,
        out Vector3 pose,
        out float score)
    {
        var point = new Vector3(targetX + 0.5f, targetY + 0.5f, targetZ + 0.5f);
        var baseX = Math.Clamp(targetX, 0, Math.Max(0, world.Width - 1));
        var baseZ = Math.Clamp(targetZ, 0, Math.Max(0, world.Depth - 1));
        var maxFeetCell = Math.Clamp((int)MathF.Floor(point.Y) + 1, 1, Math.Max(1, world.Height - 2));
        var minFeetCell = Math.Max(1, maxFeetCell - 6);
        pose = Position;
        score = float.MaxValue;

        for (var ring = 0; ring <= Math.Max(0, searchRadius); ring++)
        {
            var ringFound = false;
            var ringBestPose = Position;
            var ringBestScore = float.MaxValue;
            for (var dx = -ring; dx <= ring; dx++)
            {
                for (var dz = -ring; dz <= ring; dz++)
                {
                    if (ring > 0 && Math.Abs(dx) != ring && Math.Abs(dz) != ring)
                    {
                        continue;
                    }

                    var x = baseX + dx;
                    var z = baseZ + dz;
                    if (x < 0 || z < 0 || x >= world.Width || z >= world.Depth)
                    {
                        continue;
                    }

                    var columnTopFeet = Math.Min(world.GetTopSolidY(x, z) + 1, maxFeetCell);
                    for (var feetCell = minFeetCell; feetCell <= columnTopFeet; feetCell++)
                    {
                        var candidate = new Vector3(x + 0.5f, feetCell + 0.02f, z + 0.5f);
                        if (!IsPoseClear(world, candidate)
                            || DoesPoseOverlapBlock(candidate, targetX, targetY, targetZ)
                            || !CanActOnBlockFromPose(candidate, targetX, targetY, targetZ))
                        {
                            continue;
                        }

                        var candidateScore = ScoreActionPose(world, blueprint, targetY, x, feetCell, z, columnTopFeet);
                        if (candidateScore >= ringBestScore)
                        {
                            continue;
                        }

                        ringFound = true;
                        ringBestPose = candidate;
                        ringBestScore = candidateScore;
                    }
                }
            }

            if (ringFound)
            {
                pose = ringBestPose;
                score = ring + ringBestScore;
                return true;
            }
        }

        return false;
    }

    private static float ScoreActionPose(
        WorldMap world,
        HouseBlueprint? blueprint,
        int targetY,
        int cellX,
        int feetCell,
        int cellZ,
        int columnTopFeet)
    {
        var score = MathF.Max(0, columnTopFeet - feetCell) * 2.5f;
        score += GetActionPoseConfinementScore(world, cellX, feetCell, cellZ);

        if (blueprint is null)
        {
            return score;
        }

        var buildFeetCell = blueprint.FloorY + 1;
        score += MathF.Abs(feetCell - buildFeetCell) * 1.5f;
        if (feetCell < buildFeetCell)
        {
            score += (buildFeetCell - feetCell) * 4f;
        }

        if (blueprint.IsInsideInterior(cellX, cellZ))
        {
            score += 12f;
        }
        else if (blueprint.IsInsideFootprint(cellX, cellZ))
        {
            score += 5f;
        }

        return score;
    }

    private static float GetActionPoseConfinementScore(WorldMap world, int cellX, int feetCell, int cellZ)
    {
        var score = 0f;
        var directions = new (int X, int Z)[]
        {
            (1, 0),
            (-1, 0),
            (0, 1),
            (0, -1)
        };

        for (var i = 0; i < directions.Length; i++)
        {
            var sideX = cellX + directions[i].X;
            var sideZ = cellZ + directions[i].Z;
            var blockedAtFeet = world.IsSolid(sideX, feetCell, sideZ);
            var blockedAtBody = world.IsSolid(sideX, feetCell + 1, sideZ);
            if (blockedAtFeet)
            {
                score += 1.5f;
            }

            if (blockedAtFeet && blockedAtBody)
            {
                score += 3f;
            }
        }

        return score;
    }

    private bool DoesPoseOverlapBlock(Vector3 pose, int x, int y, int z)
    {
        var minX = pose.X - Actor.ColliderHalfWidth + 0.03f;
        var maxX = pose.X + Actor.ColliderHalfWidth - 0.03f;
        var minY = pose.Y + 0.02f;
        var maxY = pose.Y + Actor.ColliderHeight - 0.03f;
        var minZ = pose.Z - Actor.ColliderHalfWidth + 0.03f;
        var maxZ = pose.Z + Actor.ColliderHalfWidth - 0.03f;
        return maxX > x
            && minX < x + 1f
            && maxY > y
            && minY < y + 1f
            && maxZ > z
            && minZ < z + 1f;
    }

    private bool CanActOnBlockFromPose(Vector3 pose, int x, int y, int z)
    {
        var eye = pose + new Vector3(0f, Actor.ColliderHeight * 0.92f, 0f);
        var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
        var dx = center.X - eye.X;
        var dy = center.Y - eye.Y;
        var dz = center.Z - eye.Z;
        var distanceSq = dx * dx + dy * dy + dz * dz;
        return distanceSq <= ReachDistance * ReachDistance;
    }

    private bool TryFindStandPoseNear(WorldMap world, Vector3 point, int searchRadius, out Vector3 pose)
    {
        var baseX = Math.Clamp((int)MathF.Floor(point.X), 0, Math.Max(0, world.Width - 1));
        var baseZ = Math.Clamp((int)MathF.Floor(point.Z), 0, Math.Max(0, world.Depth - 1));

        for (var ring = 0; ring <= Math.Max(0, searchRadius); ring++)
        {
            for (var dx = -ring; dx <= ring; dx++)
            {
                for (var dz = -ring; dz <= ring; dz++)
                {
                    if (ring > 0 && Math.Abs(dx) != ring && Math.Abs(dz) != ring)
                    {
                        continue;
                    }

                    var x = baseX + dx;
                    var z = baseZ + dz;
                    if (x < 0 || z < 0 || x >= world.Width || z >= world.Depth)
                    {
                        continue;
                    }

                    var feetY = world.GetTopSolidY(x, z) + 1.02f;
                    var candidate = new Vector3(x + 0.5f, feetY, z + 0.5f);
                    if (!IsPoseClear(world, candidate))
                    {
                        continue;
                    }

                    pose = candidate;
                    return true;
                }
            }
        }

        pose = Position;
        return false;
    }

    private bool IsPoseClear(WorldMap world, Vector3 pose)
    {
        var half = Actor.ColliderHalfWidth;
        var height = Actor.ColliderHeight;
        if (pose.X - half < 0f
            || pose.Z - half < 0f
            || pose.X + half >= world.Width
            || pose.Z + half >= world.Depth
            || pose.Y < 0f
            || pose.Y + height >= world.Height)
        {
            return false;
        }

        var minX = (int)MathF.Floor(pose.X - half);
        var maxX = (int)MathF.Floor(pose.X + half);
        var minY = (int)MathF.Floor(pose.Y);
        var maxY = (int)MathF.Floor(pose.Y + height);
        var minZ = (int)MathF.Floor(pose.Z - half);
        var maxZ = (int)MathF.Floor(pose.Z + half);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    if (world.IsSolid(x, y, z))
                    {
                        return false;
                    }
                }
            }
        }

        return world.IsSolidAt(new Vector3(pose.X, pose.Y - 0.08f, pose.Z));
    }

    private static Vector3 ToHorizontal(Vector3 direction)
    {
        var horizontal = new Vector3(direction.X, 0f, direction.Z);
        return horizontal.LengthSquared() <= 0.00001f
            ? new Vector3(0f, 0f, -1f)
            : Vector3.Normalize(horizontal);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= MathF.PI * 2f;
        }

        while (angle < -MathF.PI)
        {
            angle += MathF.PI * 2f;
        }

        return angle;
    }

    private enum MoveResult
    {
        Arrived = 0,
        Moving = 1,
        Blocked = 2
    }

    private enum NavigationPurpose
    {
        None = 0,
        Follow = 1,
        GatherAction = 2,
        BuildAction = 3
    }
}
