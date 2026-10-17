using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

public static class ClanStore
{
    private static readonly object LockObj = new();
    private static Dictionary<string, ClanRecord> _clans = new(StringComparer.OrdinalIgnoreCase);
    private static string _filePath;

    public static readonly byte[] DefaultTransparentPng = new byte[]
    {
        0x89, 0x50, 0x4E, 0x4F, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    };

    public static void Initialize(string clusterKey)
    {
        lock (LockObj)
        {
            string dir = AppData.CreateDirectory(Path.Combine("offline", clusterKey ?? "nx"));
            _filePath = Path.Combine(dir, "clans.json");

            if (File.Exists(_filePath))
            {
                try
                {
                    string json = File.ReadAllText(_filePath);
                    _clans = JsonConvert.DeserializeObject<Dictionary<string, ClanRecord>>(json) 
                             ?? new Dictionary<string, ClanRecord>(StringComparer.OrdinalIgnoreCase);
                    Console.WriteLine($"[ClanStore] โหลดเผ่าแล้ว {_clans.Count} เผ่า จาก {_filePath}");
                    return;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[ClanStore] โหลด clans.json ล้มเหลว: {e.Message}");
                }
            }

            _clans = new Dictionary<string, ClanRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void Save()
    {
        lock (LockObj)
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            try
            {
                string json = JsonConvert.SerializeObject(_clans, Formatting.Indented);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ClanStore] บันทึก clans.json ล้มเหลว: {e.Message}");
            }
        }
    }

    public static ClanRecord GetClan(string clanId)
    {
        if (string.IsNullOrEmpty(clanId)) return null;
        lock (LockObj)
        {
            _clans.TryGetValue(clanId, out ClanRecord clan);
            return clan;
        }
    }

    public static ClanRecord GetClanByMember(string entityId)
    {
        if (string.IsNullOrEmpty(entityId)) return null;
        lock (LockObj)
        {
            return _clans.Values.FirstOrDefault(c => c.Members.Any(m => string.Equals(m.EntityId, entityId, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public static ClanRecord FindClanByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        lock (LockObj)
        {
            return _clans.Values.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static List<ClanRecord> SearchClans(string keyword)
    {
        lock (LockObj)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return _clans.Values.Take(50).ToList();
            }
            return _clans.Values
                .Where(c => c.Name != null && c.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(50)
                .ToList();
        }
    }

    public static ClanRecord CreateClan(string name, string creatorEntityId, long initialFund = 0)
    {
        lock (LockObj)
        {
            string id = "clan_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            var clan = new ClanRecord
            {
                Id = id,
                Name = name,
                Fund = initialFund,
                Level = 1,
                Exp = 0,
                Capacity = 10,
                Mainland = "아시안",
                Intro = "환영합니다! 개척자 부족입니다.",
                Notice = "부족에 오신 것을 환영합니다. 함께 영토를 개척해 나갑시다.",
                CreatedAt = Times.UnixTimeNow(),
                Members = new List<ClanMemberRecord>
                {
                    new() { EntityId = creatorEntityId, RoleId = 0 }
                },
                Roles = new Dictionary<int, ClanRoleRecord>
                {
                    [0] = new() { Id = 0, Grade = 0, Permissions = 31, UserType = 1, Name = "부족장" },
                    [1] = new() { Id = 1, Grade = 1, Permissions = 0, UserType = 0, Name = "부족원" }
                }
            };

            _clans[id] = clan;
            Save();
            Console.WriteLine($"[ClanStore] สร้างเผ่าใหม่สำเร็จ: '{name}' ({id}) โดยผู้เล่น {creatorEntityId}");
            return clan;
        }
    }

    public static bool LeaveClan(string clanId, string entityId)
    {
        lock (LockObj)
        {
            if (!_clans.TryGetValue(clanId, out ClanRecord clan)) return false;
            int removed = clan.Members.RemoveAll(m => string.Equals(m.EntityId, entityId, StringComparison.OrdinalIgnoreCase));
            if (clan.Members.Count == 0)
            {
                _clans.Remove(clanId);
                Console.WriteLine($"[ClanStore] ลบเผ่า {clanId} เนื่องจากไม่มีสมาชิกเหลืออยู่");
            }
            Save();
            return removed > 0;
        }
    }

    public static void AddFund(string clanId, long amount)
    {
        if (amount <= 0) return;
        lock (LockObj)
        {
            if (_clans.TryGetValue(clanId, out ClanRecord clan))
            {
                clan.Fund += amount;
                Save();
                Console.WriteLine($"[ClanStore] เผ่า {clan.Name} ได้รับเงินบริจาค +{amount:N0} → ยอดคงเหลือ {clan.Fund:N0} T-Stone");
            }
        }
    }

    public static bool TrySpendFund(string clanId, long amount)
    {
        if (amount <= 0) return true;
        lock (LockObj)
        {
            if (_clans.TryGetValue(clanId, out ClanRecord clan))
            {
                if (clan.Fund < amount) return false;
                clan.Fund -= amount;
                Save();
                Console.WriteLine($"[ClanStore] เผ่า {clan.Name} จ่ายเงินคลัง -{amount:N0} → ยอดคงเหลือ {clan.Fund:N0} T-Stone");
                return true;
            }
            return false;
        }
    }

    public static bool SetNoticeAndIntro(string clanId, string notice, string intro)
    {
        lock (LockObj)
        {
            if (_clans.TryGetValue(clanId, out ClanRecord clan))
            {
                if (notice != null) clan.Notice = notice;
                if (intro != null) clan.Intro = intro;
                Save();
                return true;
            }
            return false;
        }
    }

    public static bool SetEmblem(string clanId, byte[] emblem)
    {
        lock (LockObj)
        {
            if (_clans.TryGetValue(clanId, out ClanRecord clan))
            {
                clan.Emblem = emblem;
                Save();
                return true;
            }
            return false;
        }
    }

    public static bool SetMemberRole(string clanId, string entityId, int roleId)
    {
        lock (LockObj)
        {
            if (_clans.TryGetValue(clanId, out ClanRecord clan))
            {
                var member = clan.Members.FirstOrDefault(m => string.Equals(m.EntityId, entityId, StringComparison.OrdinalIgnoreCase));
                if (member != null)
                {
                    member.RoleId = roleId;
                    Save();
                    return true;
                }
            }
            return false;
        }
    }

    public static bool RenameClan(string clanId, string newName)
    {
        if (string.IsNullOrEmpty(newName)) return false;
        lock (LockObj)
        {
            if (_clans.TryGetValue(clanId, out ClanRecord clan))
            {
                clan.Name = newName;
                Save();
                return true;
            }
            return false;
        }
    }

    public static JObject ToClanJson(ClanRecord clan)
    {
        if (clan == null) return new JObject();

        var roleInfos = new JObject();
        foreach (var r in clan.Roles.Values)
        {
            roleInfos[r.Id.ToString()] = new JObject
            {
                ["id"] = r.Id,
                ["grade"] = r.Grade,
                ["permissions"] = r.Permissions,
                ["user_type"] = r.UserType,
                ["name"] = r.Name ?? ""
            };
        }

        var membersArr = new JArray();
        foreach (var m in clan.Members)
        {
            // PairConverter expects a 2-element array: [ entityId, roleId ]
            membersArr.Add(new JArray(m.EntityId, m.RoleId));
        }

        var appliersArr = new JArray();
        if (clan.Appliers != null)
        {
            foreach (var a in clan.Appliers)
            {
                appliersArr.Add(a);
            }
        }

        return new JObject
        {
            ["id"] = clan.Id,
            ["name"] = clan.Name,
            ["fund"] = clan.Fund,
            ["level"] = clan.Level,
            ["exp"] = clan.Exp,
            ["intro"] = clan.Intro ?? "",
            ["notice"] = clan.Notice ?? "",
            ["member_count"] = clan.Members.Count,
            ["capacity"] = clan.Capacity,
            ["mainland"] = clan.Mainland ?? "아시안",
            ["members"] = membersArr,
            ["appliers"] = appliersArr,
            ["role_infos"] = roleInfos,
            ["renameable_until"] = 0.0,
            ["emblem_changeable_until"] = 0.0
        };
    }

    public static JObject ToClanContainerJson(IEnumerable<ClanRecord> clans)
    {
        var arr = new JArray();
        if (clans != null)
        {
            foreach (var c in clans)
            {
                arr.Add(ToClanJson(c));
            }
        }
        return new JObject { ["clans"] = arr };
    }
}
