using System.Collections.Generic;
using Newtonsoft.Json;

namespace Durango.Online;

public class ClanMemberRecord
{
    [JsonProperty("entity_id")]
    public string EntityId { get; set; }

    [JsonProperty("role_id")]
    public int RoleId { get; set; }
}

public class ClanRoleRecord
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("grade")]
    public int Grade { get; set; }

    [JsonProperty("permissions")]
    public int Permissions { get; set; }

    [JsonProperty("user_type")]
    public int UserType { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }
}

public class ClanRecord
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("fund")]
    public long Fund { get; set; }

    [JsonProperty("level")]
    public int Level { get; set; } = 1;

    [JsonProperty("exp")]
    public long Exp { get; set; }

    [JsonProperty("intro")]
    public string Intro { get; set; } = "";

    [JsonProperty("notice")]
    public string Notice { get; set; } = "";

    [JsonProperty("capacity")]
    public int Capacity { get; set; } = 10;

    [JsonProperty("mainland")]
    public string Mainland { get; set; } = "아시안";

    [JsonProperty("members")]
    public List<ClanMemberRecord> Members { get; set; } = new();

    [JsonProperty("appliers")]
    public List<string> Appliers { get; set; } = new();

    [JsonProperty("roles")]
    public Dictionary<int, ClanRoleRecord> Roles { get; set; } = new();

    [JsonProperty("emblem")]
    public byte[] Emblem { get; set; }

    [JsonProperty("created_at")]
    public double CreatedAt { get; set; }
}
