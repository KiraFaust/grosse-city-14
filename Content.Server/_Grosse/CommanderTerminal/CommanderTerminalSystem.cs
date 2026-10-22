using System.Linq;
using System.Numerics;
using Content.Server.Pinpointer;
using Content.Server.Radio.EntitySystems;
using Content.Server.Roles.Jobs;
using Content.Server.Station.Systems;
using Content.Shared._Grosse.CommanderTerminal;
using Content.Shared._Grosse.ZLevels.Core.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Pinpointer;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Grosse.CommanderTerminal;

public sealed partial class CommanderTerminalSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private NavMapSystem _navMap = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private JobSystem _jobs = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, CommanderScopeData> _scopes = new();
    private TimeSpan _nextUpdate;

    private const int MaxGroupNameLength = 24;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CommanderTerminalComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<CommanderTerminalComponent, CreateCommanderGroupMessage>(OnCreateGroup);
        SubscribeLocalEvent<CommanderTerminalComponent, RenameCommanderGroupMessage>(OnRenameGroup);
        SubscribeLocalEvent<CommanderTerminalComponent, DeleteCommanderGroupMessage>(OnDeleteGroup);
        SubscribeLocalEvent<CommanderTerminalComponent, AssignCommanderMemberMessage>(OnAssignMember);
        SubscribeLocalEvent<CommanderTerminalComponent, RemoveCommanderMemberMessage>(OnRemoveMember);
        SubscribeLocalEvent<CommanderTerminalComponent, SetCommanderLeaderMessage>(OnSetLeader);
        SubscribeLocalEvent<CommanderTerminalComponent, IssueCommanderOrderMessage>(OnIssueOrder);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(2);
        UpdateOpenTerminals();
    }

    private void OnUiOpened(Entity<CommanderTerminalComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnCreateGroup(Entity<CommanderTerminalComponent> ent, ref CreateCommanderGroupMessage args)
    {
        if (!TryGetScope(ent, out var scope))
            return;

        var name = SanitizeName(args.Name);
        if (name == null)
            return;

        var data = GetScope(scope);
        data.Groups.Add(new CommanderGroupData(Guid.NewGuid().ToString("N"), name, data.NextColor));
        data.NextColor++;
        UpdateOpenTerminals();
    }

    private void OnRenameGroup(Entity<CommanderTerminalComponent> ent, ref RenameCommanderGroupMessage args)
    {
        if (!TryGetGroup(ent, args.GroupId, out var group))
            return;

        var name = SanitizeName(args.Name);
        if (name == null)
            return;

        group.Name = name;
        UpdateOpenTerminals();
    }

    private void OnDeleteGroup(Entity<CommanderTerminalComponent> ent, ref DeleteCommanderGroupMessage args)
    {
        if (!TryGetScope(ent, out var scope) || !_scopes.TryGetValue(scope, out var data))
            return;

        var groupId = args.GroupId;
        data.Groups.RemoveAll(group => group.GroupId == groupId);
        UpdateOpenTerminals();
    }

    private void OnAssignMember(Entity<CommanderTerminalComponent> ent, ref AssignCommanderMemberMessage args)
    {
        if (!TryGetScope(ent, out var scope) || !_scopes.TryGetValue(scope, out var data))
            return;

        var groupId = args.GroupId;
        var memberId = args.MemberId;
        var target = data.Groups.FirstOrDefault(group => group.GroupId == groupId);
        if (target == null || !IsKnownMember(ent, memberId) || target.Members.Contains(memberId))
            return;

        foreach (var group in data.Groups)
        {
            if (!group.Members.Remove(memberId))
                continue;

            if (group.LeaderId == memberId)
                group.LeaderId = null;
        }

        target.Members.Add(memberId);
        UpdateOpenTerminals();
    }

    private void OnRemoveMember(Entity<CommanderTerminalComponent> ent, ref RemoveCommanderMemberMessage args)
    {
        if (!TryGetScope(ent, out var scope) || !_scopes.TryGetValue(scope, out var data))
            return;

        foreach (var group in data.Groups)
        {
            if (!group.Members.Remove(args.MemberId))
                continue;

            if (group.LeaderId == args.MemberId)
                group.LeaderId = null;
        }

        UpdateOpenTerminals();
    }

    private void OnSetLeader(Entity<CommanderTerminalComponent> ent, ref SetCommanderLeaderMessage args)
    {
        if (!TryGetGroup(ent, args.GroupId, out var group))
            return;

        if (!group.Members.Contains(args.MemberId) || !IsKnownMember(ent, args.MemberId))
            return;

        group.LeaderId = args.MemberId;
        UpdateOpenTerminals();
    }

    private void OnIssueOrder(Entity<CommanderTerminalComponent> ent, ref IssueCommanderOrderMessage args)
    {
        if (!TryGetGroup(ent, args.GroupId, out var group))
        {
            Popup(ent, args.Actor, "commander-terminal-no-group");
            return;
        }

        if (group.LeaderId == null || !IsKnownMember(ent, group.LeaderId))
        {
            Popup(ent, args.Actor, "commander-terminal-no-leader");
            return;
        }

        var gridNet = args.Grid;
        var grid = GetEntity(gridNet);
        if (!Exists(grid) || !GetFloors(ent).Any(floor => floor.Grid == gridNet))
            return;

        var coordinates = new MapCoordinates(args.Position, Transform(grid).MapID);
        var location = FormattedMessage.RemoveMarkupOrThrow(_navMap.GetNearestBeaconString(coordinates));
        var message = Loc.GetString("commander-terminal-order", ("group", group.Name), ("location", location));
        _radio.SendRadioMessage(ent.Owner, message, ent.Comp.RadioChannel, ent.Owner);
        Popup(ent, args.Actor, "commander-terminal-order-sent", ("group", group.Name));
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _scopes.Clear();
    }

    private void UpdateOpenTerminals()
    {
        var query = EntityQueryEnumerator<CommanderTerminalComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, CommanderTerminalUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<CommanderTerminalComponent> ent)
    {
        if (!TryGetScope(ent, out var scope))
            return;

        var members = CollectMembers(ent, scope);
        var data = GetScope(scope);
        var groups = new List<CommanderGroup>(data.Groups.Count);

        foreach (var group in data.Groups)
        {
            groups.Add(new CommanderGroup(
                group.GroupId,
                group.Name,
                group.LeaderId,
                group.ColorIndex,
                group.Members.ToList()));
        }

        var station = _station.GetOwningStation(ent);
        var stationName = station == null
            ? Loc.GetString("commander-terminal-unknown-station")
            : Name(station.Value);

        var consoleGrid = Transform(ent).GridUid;
        var state = new CommanderTerminalState(
            stationName,
            consoleGrid == null ? null : GetNetEntity(consoleGrid.Value),
            members,
            groups,
            GetFloors(ent));

        _ui.SetUiState(ent.Owner, CommanderTerminalUiKey.Key, state);
    }

    private List<CommanderMember> CollectMembers(Entity<CommanderTerminalComponent> ent, EntityUid scope)
    {
        if (!_prototypes.TryIndex(ent.Comp.Department, out DepartmentPrototype? department))
            return new List<CommanderMember>();

        var membership = MembershipLookup(scope);
        var members = new List<CommanderMember>();
        var minds = EntityQueryEnumerator<MindComponent>();

        while (minds.MoveNext(out var mindId, out var mind))
        {
            if (mind.UserId == null || mind.CurrentEntity is not { } body)
                continue;

            if (!Exists(body) || !HasComp<MobStateComponent>(body) || _mobState.IsDead(body) || !IsInScope(body, scope))
                continue;

            if (!_jobs.MindTryGetJobId(mindId, out var jobId) || jobId is not { } job)
                continue;

            if (!department.Roles.Contains(job))
                continue;

            var memberId = GetNetEntity(body).ToString();
            membership.TryGetValue(memberId, out var assigned);
            _jobs.MindTryGetJobName(mindId, out var jobName);

            var name = string.IsNullOrWhiteSpace(mind.CharacterName) ? Name(body) : mind.CharacterName;
            members.Add(new CommanderMember(
                memberId,
                GetNetEntity(body),
                name,
                jobName,
                _navMap.GetNearestBeaconString(body, onlyName: true),
                GetNetCoordinates(_transform.GetMoverCoordinates(body)),
                assigned?.GroupId,
                assigned?.LeaderId == memberId));
        }

        members.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return members;
    }

    private Dictionary<string, CommanderGroupData> MembershipLookup(EntityUid scope)
    {
        var lookup = new Dictionary<string, CommanderGroupData>();
        if (!_scopes.TryGetValue(scope, out var data))
            return lookup;

        foreach (var group in data.Groups)
        {
            foreach (var memberId in group.Members)
                lookup[memberId] = group;
        }

        return lookup;
    }

    private bool IsKnownMember(Entity<CommanderTerminalComponent> ent, string memberId)
    {
        if (!TryGetScope(ent, out var scope))
            return false;

        return CollectMembers(ent, scope).Any(member => member.MemberId == memberId);
    }

    private List<CommanderFloor> GetFloors(EntityUid console)
    {
        var floors = new List<CommanderFloor>();
        var mapUid = Transform(console).MapUid;

        if (mapUid != null
            && TryComp<GrosseZMapComponent>(mapUid.Value, out var zMap)
            && TryComp<GrosseZMapNetworkComponent>(zMap.NetworkUid, out var network))
        {
            var depths = network.ZLevels.Keys.ToList();
            depths.Sort();

            foreach (var depth in depths)
            {
                if (network.ZLevels[depth] is not { } levelMap)
                    continue;

                if (TryFindNavGrid(levelMap, out var grid))
                    floors.Add(new CommanderFloor(depth, GetNetEntity(grid)));
            }
        }

        if (floors.Count == 0 && Transform(console).GridUid is { } consoleGrid && HasComp<NavMapComponent>(consoleGrid))
            floors.Add(new CommanderFloor(0, GetNetEntity(consoleGrid)));

        return floors;
    }

    private bool TryFindNavGrid(EntityUid map, out EntityUid grid)
    {
        EntityUid? stationGrid = null;
        EntityUid? anyGrid = null;
        var query = EntityQueryEnumerator<NavMapComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != map)
                continue;

            anyGrid ??= uid;
            if (HasComp<StationMemberComponent>(uid))
                stationGrid ??= uid;
        }

        grid = stationGrid ?? anyGrid ?? EntityUid.Invalid;
        return grid.IsValid();
    }

    private bool TryGetScope(EntityUid console, out EntityUid scope)
    {
        var map = Transform(console).MapUid;
        if (map != null && TryComp<GrosseZMapComponent>(map.Value, out var zMap) && zMap.NetworkUid.IsValid())
        {
            scope = zMap.NetworkUid;
            return true;
        }

        var station = _station.GetOwningStation(console);
        if (station != null)
        {
            scope = station.Value;
            return true;
        }

        if (map != null)
        {
            scope = map.Value;
            return true;
        }

        scope = EntityUid.Invalid;
        return false;
    }

    private bool IsInScope(EntityUid body, EntityUid scope)
    {
        if (_station.GetOwningStation(body) == scope)
            return true;

        var map = Transform(body).MapUid;
        if (map == null)
            return false;

        if (map == scope)
            return true;

        return TryComp<GrosseZMapComponent>(map.Value, out var zMap) && zMap.NetworkUid == scope;
    }

    private bool TryGetGroup(EntityUid console, string groupId, out CommanderGroupData group)
    {
        group = null!;
        if (!TryGetScope(console, out var scope) || !_scopes.TryGetValue(scope, out var data))
            return false;

        var found = data.Groups.FirstOrDefault(candidate => candidate.GroupId == groupId);
        if (found == null)
            return false;

        group = found;
        return true;
    }

    private CommanderScopeData GetScope(EntityUid scope)
    {
        if (!_scopes.TryGetValue(scope, out var data))
        {
            data = new CommanderScopeData();
            _scopes[scope] = data;
        }

        return data;
    }

    private void Popup(EntityUid console, EntityUid actor, string message, params (string, object)[] args)
    {
        if (!Exists(actor))
            return;

        var text = args.Length == 0 ? Loc.GetString(message) : Loc.GetString(message, args);
        _popup.PopupEntity(text, console, actor);
    }

    private static string? SanitizeName(string input)
    {
        var sanitized = FormattedMessage.RemoveMarkupPermissive(input).Trim();
        if (sanitized.Length == 0)
            return null;

        if (sanitized.Length > MaxGroupNameLength)
            sanitized = sanitized[..MaxGroupNameLength];

        return sanitized;
    }
}

public sealed class CommanderScopeData
{
    public List<CommanderGroupData> Groups { get; } = new();
    public int NextColor;
}

public sealed class CommanderGroupData
{
    public string GroupId { get; }
    public string Name;
    public string? LeaderId;
    public int ColorIndex { get; }
    public List<string> Members { get; } = new();

    public CommanderGroupData(string groupId, string name, int colorIndex)
    {
        GroupId = groupId;
        Name = name;
        ColorIndex = colorIndex;
    }
}
