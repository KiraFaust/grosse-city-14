using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Grosse.CommanderTerminal;

[Serializable, NetSerializable]
public enum CommanderTerminalUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class CommanderTerminalState : BoundUserInterfaceState
{
    public string StationName { get; }
    public NetEntity? ConsoleGrid { get; }
    public List<CommanderMember> Members { get; }
    public List<CommanderGroup> Groups { get; }
    public List<CommanderFloor> Floors { get; }

    public CommanderTerminalState(
        string stationName,
        NetEntity? consoleGrid,
        List<CommanderMember> members,
        List<CommanderGroup> groups,
        List<CommanderFloor> floors)
    {
        StationName = stationName;
        ConsoleGrid = consoleGrid;
        Members = members;
        Groups = groups;
        Floors = floors;
    }
}

[Serializable, NetSerializable]
public sealed class CommanderMember
{
    public string MemberId { get; }
    public NetEntity Body { get; }
    public string Name { get; }
    public string JobTitle { get; }
    public string Location { get; }
    public NetCoordinates Coordinates { get; }
    public string? GroupId { get; }
    public bool IsLeader { get; }

    public CommanderMember(
        string memberId,
        NetEntity body,
        string name,
        string jobTitle,
        string location,
        NetCoordinates coordinates,
        string? groupId,
        bool isLeader)
    {
        MemberId = memberId;
        Body = body;
        Name = name;
        JobTitle = jobTitle;
        Location = location;
        Coordinates = coordinates;
        GroupId = groupId;
        IsLeader = isLeader;
    }
}

[Serializable, NetSerializable]
public sealed class CommanderGroup
{
    public string GroupId { get; }
    public string Name { get; }
    public string? LeaderId { get; }
    public int ColorIndex { get; }
    public List<string> Members { get; }

    public CommanderGroup(string groupId, string name, string? leaderId, int colorIndex, List<string> members)
    {
        GroupId = groupId;
        Name = name;
        LeaderId = leaderId;
        ColorIndex = colorIndex;
        Members = members;
    }
}

[Serializable, NetSerializable]
public sealed class CommanderFloor
{
    public int Depth { get; }
    public NetEntity Grid { get; }

    public CommanderFloor(int depth, NetEntity grid)
    {
        Depth = depth;
        Grid = grid;
    }
}

[Serializable, NetSerializable]
public sealed class CreateCommanderGroupMessage : BoundUserInterfaceMessage
{
    public string Name { get; }

    public CreateCommanderGroupMessage(string name)
    {
        Name = name;
    }
}

[Serializable, NetSerializable]
public sealed class RenameCommanderGroupMessage : BoundUserInterfaceMessage
{
    public string GroupId { get; }
    public string Name { get; }

    public RenameCommanderGroupMessage(string groupId, string name)
    {
        GroupId = groupId;
        Name = name;
    }
}

[Serializable, NetSerializable]
public sealed class DeleteCommanderGroupMessage : BoundUserInterfaceMessage
{
    public string GroupId { get; }

    public DeleteCommanderGroupMessage(string groupId)
    {
        GroupId = groupId;
    }
}

[Serializable, NetSerializable]
public sealed class AssignCommanderMemberMessage : BoundUserInterfaceMessage
{
    public string GroupId { get; }
    public string MemberId { get; }

    public AssignCommanderMemberMessage(string groupId, string memberId)
    {
        GroupId = groupId;
        MemberId = memberId;
    }
}

[Serializable, NetSerializable]
public sealed class RemoveCommanderMemberMessage : BoundUserInterfaceMessage
{
    public string MemberId { get; }

    public RemoveCommanderMemberMessage(string memberId)
    {
        MemberId = memberId;
    }
}

[Serializable, NetSerializable]
public sealed class SetCommanderLeaderMessage : BoundUserInterfaceMessage
{
    public string GroupId { get; }
    public string MemberId { get; }

    public SetCommanderLeaderMessage(string groupId, string memberId)
    {
        GroupId = groupId;
        MemberId = memberId;
    }
}

[Serializable, NetSerializable]
public sealed class IssueCommanderOrderMessage : BoundUserInterfaceMessage
{
    public string GroupId { get; }
    public NetEntity Grid { get; }
    public Vector2 Position { get; }

    public IssueCommanderOrderMessage(string groupId, NetEntity grid, Vector2 position)
    {
        GroupId = groupId;
        Grid = grid;
        Position = position;
    }
}
