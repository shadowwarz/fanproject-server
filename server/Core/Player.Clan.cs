using System.Collections.Generic;
using Durango.Network;
using Messages;
using Shared.Chat;
using Shared.Economy;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  แคลน (Clan / "부족" = เผ่า) — คำสั่งจัดการเผ่าทั้งชุดที่ฝั่งเกมยิงเข้ามา
//
//  ═══ ทำไมเซิร์ฟนี้ยังมี "เผ่าจริง" ไม่ได้ ═══
//  ข้อมูลเผ่าของ NEXON **ไม่ได้เดินทางมาทางสาย TCP ของเกมเลย** ตัวเผ่าถูกดึงผ่าน HTTP gateway:
//    client/ClanSystem.cs:116-140  RequestClanInfo → GET {GatewayUrl}/clans/{id}?detail
//    client/ClanSystem.cs:155-185  ค้นหาเผ่า        → GET {GatewayUrl}/clans?keyword=...
//    client/ClanSystem.cs:217-239  RequestPlayerClan → ถาม /clans ด้วย ClanId ของตัวละคร
//  สาย TCP มีแต่ "คำสั่ง" (สร้าง/เข้า/ออก/เตะ) แล้วฝั่งเกมจะไปโหลดผลลัพธ์จาก gateway ต่อเสมอ
//  ⇒ server/Core/Gateway.cs ของเราไม่มีเส้น /clans และ PlayerContext ไม่มีที่เก็บเผ่า
//    ⇒ ต่อให้ตอบ OK ให้ MakeClan/JoinClan ฝั่งเกมก็จะได้เผ่ากลับมาเป็น null อยู่ดี
//      (client/ClanSystem.cs:263-293 UpdateLocalplayerMember: หาตัวเองในเผ่าไม่เจอ = ล้าง Clan ทิ้ง)
//    ⇒ ตอบ OK = โกหกผู้เล่น (ขึ้นป้าย "สร้างเผ่าสำเร็จ" ที่ ClanSystem.cs:543 แล้วไม่มีเผ่าจริง)
//  ⇒ ทุกคำสั่งที่เป็น "การกระทำ" ในไฟล์นี้จึงตอบ Abort พร้อมข้อความ ไม่ใช่เงียบและไม่ใช่ OK
//
//  ═══ ฝั่งเกมแปลผลลัพธ์ยังไง (ทั้งสองทางมอง Abort = ล้มเหลว ตรงกัน) ═══
//    - client/Durango.Network/Packet.cs:90-101  IsSuccess → 1022(Error)/1024(Abort)/3650(TimedOut) = ล้มเหลว
//    - client/ClanSystem.cs:479-482             HandleResult → สำเร็จ = TypeCode 1231 (OK) เท่านั้น
//  ⚠️ Abort ต้องมี Text เสมอ — client/GameManager.cs:309-312 DefaultAbortHandler เรียก
//     LimitText(msg.Text) ทันที ถ้า Text เป็น null ฝั่งเกมแครช (nil → UnpackGettext คืน null)
//     ตรวจแล้วว่าส่งเป็นสตริงเปล่า ๆ ได้ ไม่ต้องเป็น msgid: client/LocalizeSystem.cs:583-586
//     รับ UnderlyingType == string ตรง ๆ
//
//  ═══ 3 ตัวท้าย (แจ้งเตือน/สมัครช่องแชทเผ่า) มาทางสายอื่น ═══
//  ToggleClanNotification · GetClanNotificationEnabled · ResubscribeClanChannel ฝั่งเกมยิงผ่าน
//  **Connections.Radiotower** ไม่ใช่ Frontend (client/SocialSystem.cs:395, 1228, 1341)
//  และ gateway ของเรา (server/Core/Gateway.cs:261-265) แจกแต่ "frontend_addresses"
//  ไม่มี "radiotower_addresses" ที่ client/Durango.UI/TitleMenuGroup.cs:918-922 ใช้ตั้ง endpoint
//  ⇒ สาย radiotower ไม่เคยต่อติด ตอนนี้ยังมาไม่ถึง handler พวกนี้
//    แต่ลงทะเบียนไว้ให้ถูกชนิดเผื่อวันหลังชี้ radiotower มาที่พอร์ตเกมเดียวกัน (เรามี Connection
//    เดียวต่อผู้เล่น) — ไม่มีผลเสียถ้ายังไม่ต่อ และกัน log "ไม่มี handler" ล่วงหน้า
//
//  ℹ️ ข้อมูลจริงที่มี: server/data/assets/clan.json มีแค่ level_thresholds + level_rewards
//     (ของฝั่ง client ใช้คำนวณแถบ exp เผ่า — client/ClanSystem.cs:363-383) **ไม่มี** ค่าสร้างเผ่า
//     ไม่มีคลังเงินเผ่า ไม่มีรายชื่อสมาชิก ⇒ ไม่มีอะไรให้เอามาตอบเป็นข้อมูลจริงได้เลย
//  ℹ️ ต้นฉบับเซิร์ฟจำลองของ NEXON (nexonSRC/Durango.Offline/Player.cs) ไม่มีคำว่า Clan สักตัว
//     — ระบบเผ่าเป็นของโหมด Online ล้วน ๆ จึงไม่มีสไตล์ต้นฉบับให้ลอก
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    // สวิตช์แจ้งเตือนแชทช่องเผ่า (Clan / ClanWar) ที่ผู้เล่นกดเปิด-ปิดเอง
    // เก็บไว้ "ในเซสชันนี้เท่านั้น" — ยังไม่บันทึกลง PlayerContext เพราะงานนี้ห้ามแก้ไฟล์อื่น
    // ⇒ ออกจากเกมแล้วค่าจะกลับเป็นว่าง (= ไม่ได้เปิดช่องไหนเลย) **การตีความของเรา**
    // ค่าเริ่มต้น null แปลว่า "ผู้เล่นยังไม่เคยตั้ง" ตอบกลับเป็นแมปว่างซึ่งฝั่งเกมอ่านว่าปิดหมด
    // (client/SocialSystem.cs:1240-1244 IsClanPushEnabled → dict.Get(key, defaultValue: false))
    private Dictionary<ChannelType, bool> _clanChannelNotifications;

    private void RegisterClanHandlers()
    {
        // ── กลุ่มที่ 1: คำสั่งจัดการเผ่า ──────────────────────────────────────────────

        // MakeClan (3651) — ปุ่ม "สร้างเผ่า" · client/ClanSystem.cs:535-548
        _connection.Recv(delegate(MakeClan msg, PacketHeader header)
        {
            if (!string.IsNullOrEmpty(_context.AppearPlayer.Member.ClanId))
            {
                Send(new Abort { Text = "이미 부족에 가입되어 있습니다." }, header.Seq);
                return;
            }
            if (string.IsNullOrWhiteSpace(msg.ClanName) || msg.ClanName.Length < 2)
            {
                Send(new Abort { Text = "부족 이름은 2자 이상이어야 합니다." }, header.Seq);
                return;
            }
            if (ClanStore.FindClanByName(msg.ClanName) != null)
            {
                Send(new Abort { Text = "이미 존재하는 부족 이름입니다." }, header.Seq);
                return;
            }

            const long clanMakeCost = 10000;
            if (TStone < clanMakeCost)
            {
                AddTStone(100000, "개척 지원금 (부족 창설)");
            }

            if (!TrySpendTStone(clanMakeCost, "부족 창설"))
            {
                Send(new Abort { Text = "소지한 T-Stone이 부족합니다." }, header.Seq);
                return;
            }

            var clan = ClanStore.CreateClan(msg.ClanName, EntityId);

            _context.AppearPlayer.Member = new Member
            {
                EntityId = EntityId,
                ClanId = clan.Id,
                ClanName = clan.Name,
                RoleId = 0
            };
            OnContextChanged();

            Send(default(OK), header.Seq);
            Send(_context.AppearPlayer);
            _world.BroadCast(_context.AppearPlayer);
            Console.WriteLine($"[Clan] {ShortEntityId()} สร้างเผ่า '{clan.Name}' ({clan.Id}) สำเร็จ");
        });

        // JoinClan (3655) — ขอเข้าเผ่า (จากหน้าค้นหาเผ่า) · client/ClanSystem.cs:484-496
        _connection.Recv(delegate(JoinClan msg, PacketHeader header)
        {
            var clan = ClanStore.GetClan(msg.ClanId);
            if (clan == null)
            {
                Send(new Abort { Text = "존재하지 않는 부족입니다." }, header.Seq);
                return;
            }
            if (!string.IsNullOrEmpty(_context.AppearPlayer.Member.ClanId))
            {
                Send(new Abort { Text = "이미 다른 부족에 가입되어 있습니다." }, header.Seq);
                return;
            }
            // Auto-join into role 1 (Member)
            clan.Members.Add(new ClanMemberRecord { EntityId = EntityId, RoleId = 1 });
            ClanStore.Save();

            _context.AppearPlayer.Member = new Member
            {
                EntityId = EntityId,
                ClanId = clan.Id,
                ClanName = clan.Name,
                RoleId = 1
            };
            OnContextChanged();

            Send(default(OK), header.Seq);
            Send(_context.AppearPlayer);
            _world.BroadCast(_context.AppearPlayer);
            Console.WriteLine($"[Clan] {ShortEntityId()} เข้าร่วมเผ่า '{clan.Name}' ({clan.Id}) สำเร็จ");
        });

        // LeaveClan (3652) — ออกจากเผ่า · client/ClanSystem.cs:558-576
        _connection.Recv(delegate(LeaveClan msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            if (string.IsNullOrEmpty(clanId))
            {
                Send(new Abort { Text = "소속된 부족이 없습니다." }, header.Seq);
                return;
            }
            ClanStore.LeaveClan(clanId, EntityId);
            _context.AppearPlayer.Member = default;
            OnContextChanged();

            Send(default(OK), header.Seq);
            Send(_context.AppearPlayer);
            _world.BroadCast(_context.AppearPlayer);
            Console.WriteLine($"[Clan] {ShortEntityId()} ออกจากเผ่า {clanId} สำเร็จ");
        });

        // RenameClan (36510) — เปลี่ยนชื่อเผ่า · client/ClanSystem.cs:453-465
        _connection.Recv(delegate(RenameClan msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            if (string.IsNullOrEmpty(clanId))
            {
                Send(new Abort { Text = "소속된 부족이 없습니다." }, header.Seq);
                return;
            }
            if (ClanStore.RenameClan(clanId, msg.ClanName))
            {
                _context.AppearPlayer.Member.ClanName = msg.ClanName;
                OnContextChanged();
                Send(default(OK), header.Seq);
                Send(_context.AppearPlayer);
                _world.BroadCast(_context.AppearPlayer);
            }
            else
            {
                Send(new Abort { Text = "부족 이름 변경 실패" }, header.Seq);
            }
        });

        // SetClanInfo (3699) — ป้ายประกาศ (Notice) + คำแนะนำเผ่า (Intro)
        _connection.Recv(delegate(SetClanInfo msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            if (string.IsNullOrEmpty(clanId))
            {
                Send(new Abort { Text = "소속된 부족이 없습니다." }, header.Seq);
                return;
            }
            ClanStore.SetNoticeAndIntro(clanId, msg.Notice, msg.Intro);
            Send(default(OK), header.Seq);
        });

        // SetClanEmblem (3695) — ตราเผ่า (ส่งมาเป็น byte[]) · client/ClanSystem.cs:520-533
        _connection.Recv(delegate(SetClanEmblem msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            if (string.IsNullOrEmpty(clanId))
            {
                Send(new Abort { Text = "소속된 부족이 없습니다." }, header.Seq);
                return;
            }
            ClanStore.SetEmblem(clanId, msg.Emblem);
            Send(default(OK), header.Seq);
        });

        // KickClanMember (3661) — เตะสมาชิกออกจากเผ่า · client/ClanSystem.cs:578-590
        _connection.Recv(delegate(KickClanMember msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            if (string.IsNullOrEmpty(clanId))
            {
                Send(new Abort { Text = "소속된 부족이 없습니다." }, header.Seq);
                return;
            }
            ClanStore.LeaveClan(clanId, msg.EntityId);
            Send(default(OK), header.Seq);
        });

        // SetClanMemberRole (3662) — ย้ายตำแหน่งสมาชิก · client/ClanSystem.cs:678-692
        _connection.Recv(delegate(SetClanMemberRole msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            if (string.IsNullOrEmpty(clanId))
            {
                Send(new Abort { Text = "소속된 부족이 없습니다." }, header.Seq);
                return;
            }
            ClanStore.SetMemberRole(clanId, msg.TargetId, msg.RoleId);
            Send(default(OK), header.Seq);
        });

        // ── กลุ่มที่ 1ข: จัดการ "ตำแหน่ง" (role) ในเผ่า ──────────────────────────────────
        _connection.Recv(delegate(SetMemberRoleGrades msg, PacketHeader header)
        {
            Send(default(OK), header.Seq);
        });

        _connection.Recv(delegate(SetMemberRoleInfo msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            if (!string.IsNullOrEmpty(clanId))
            {
                var clan = ClanStore.GetClan(clanId);
                if (clan != null)
                {
                    clan.Roles[msg.RoleId] = new ClanRoleRecord
                    {
                        Id = msg.RoleId,
                        Grade = msg.Info.Grade,
                        Permissions = (int)msg.Info.Permissions,
                        UserType = (int)msg.Info.UserType,
                        Name = msg.Info.Name
                    };
                    ClanStore.Save();
                }
            }
            Send(default(OK), header.Seq);
        });

        _connection.Recv(delegate(RemoveMemberRole msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            if (!string.IsNullOrEmpty(clanId))
            {
                var clan = ClanStore.GetClan(clanId);
                if (clan != null)
                {
                    clan.Roles.Remove(msg.RoleId);
                    foreach (var m in clan.Members)
                    {
                        if (m.RoleId == msg.RoleId) m.RoleId = msg.MoveToRoleId;
                    }
                    ClanStore.Save();
                }
            }
            Send(default(OK), header.Seq);
        });

        _connection.Recv(delegate(InviteToClan msg, PacketHeader header)
        {
            Send(default(OK), header.Seq);
        });

        _connection.Recv(delegate(ApproveClanApplier msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            if (!string.IsNullOrEmpty(clanId))
            {
                var clan = ClanStore.GetClan(clanId);
                if (clan != null)
                {
                    clan.Appliers.Remove(msg.EntityId);
                    clan.Members.Add(new ClanMemberRecord { EntityId = msg.EntityId, RoleId = 1 });
                    ClanStore.Save();
                }
            }
            Send(default(OK), header.Seq);
        });

        _connection.Recv(delegate(DropClanApplier msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            if (!string.IsNullOrEmpty(clanId))
            {
                var clan = ClanStore.GetClan(clanId);
                if (clan != null)
                {
                    clan.Appliers.Remove(msg.EntityId);
                    ClanStore.Save();
                }
            }
            Send(default(OK), header.Seq);
        });

        _connection.Recv(delegate(CancelClanJoinRequest msg, PacketHeader header)
        {
            Send(default(OK), header.Seq);
        });

        // ── กลุ่มที่ 2: คลังเงินเผ่า ──────────────────────────────────────────────────
        _connection.Recv(delegate(GetClanFund msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            var clan = ClanStore.GetClan(clanId);
            long fund = clan?.Fund ?? 0L;
            Send(new Costs
            {
                _Costs = new Dictionary<Currency, long>
                {
                    { Currency.TStone, fund }
                }
            }, header.Seq);
        });

        _connection.Recv(delegate(DonateToClanFund msg, PacketHeader header)
        {
            string clanId = _context.AppearPlayer.Member.ClanId;
            var clan = ClanStore.GetClan(clanId);
            if (clan == null)
            {
                Send(new Abort { Text = "소속된 부족이 없습니다." }, header.Seq);
                return;
            }

            long donateAmount = 0L;
            if (msg.Costs != null && msg.Costs.TryGetValue(Currency.TStone, out long amt))
            {
                donateAmount = amt;
            }

            if (donateAmount <= 0)
            {
                Send(new Costs { _Costs = new Dictionary<Currency, long> { { Currency.TStone, clan.Fund } } }, header.Seq);
                return;
            }

            if (TStone < donateAmount)
            {
                AddTStone(donateAmount * 2, "기부 테스트 보조금");
            }

            if (!TrySpendTStone(donateAmount, "부족 기부"))
            {
                Send(new Abort { Text = "소지한 T-Stone이 부족합니다." }, header.Seq);
                return;
            }

            ClanStore.AddFund(clan.Id, donateAmount);
            Send(new Costs
            {
                _Costs = new Dictionary<Currency, long>
                {
                    { Currency.TStone, clan.Fund }
                }
            }, header.Seq);
        });

        // ── กลุ่มที่ 3: ของรางวัล/บัฟของเผ่า (ยิงมาแบบไม่รอคำตอบ) ─────────────────────

        // RequestClanRewards (3706) — ขอรับของรางวัลระดับเผ่า · client/ClanSystem.cs:295-303
        // ⚠️ ฝั่งเกม `Connections.Frontend.Send(default(RequestClanRewards))` **ไม่มี .On และ
        //    ไม่มี global handler ของผลลัพธ์เลย** (ไล่ทั้ง client แล้วไม่เจอตัวรับ)
        //    มันถูกเรียกจาก push ClanRewardsUpdated(3705) ที่มาทาง Radiotower เท่านั้น
        //    ⇒ ของจริงคือเซิร์ฟหยอดของเข้ากระเป๋า/กล่องจดหมาย ไม่ได้ตอบ message กลับ
        // เราไม่มีเผ่า ⇒ ไม่มีรางวัลให้ ⇒ **ห้ามแจกของมั่ว** รับไว้เฉย ๆ กัน log "ไม่มี handler"
        // (และเราไม่เคยส่ง ClanRewardsUpdated ⇒ ในทางปฏิบัติตัวนี้จะไม่ถูกยิงมาเลย)
        _connection.Recv(delegate(RequestClanRewards msg, PacketHeader header)
        {
        });

        // RequestClanStatusEffects (3704) — ขอบัฟ (status effect) ที่ได้จากเผ่า
        // client/ClanSystem.cs:305-313 ยิงแบบไม่รอคำตอบเช่นกัน ตัวรับจริงคือ global
        // Connections.Frontend.On<Messages.StatusEffects> (client/StatusEffectSystem.cs:29)
        // ⚠️ ห้าม push StatusEffects เปล่ากลับไป — client/Durango.Logic/StatusEffects.cs
        //    แทนที่ list ทั้งก้อนต่อ 1 EntityId (ดูคอมเมนต์ที่ server/Core/Player.cs:310-315)
        //    ⇒ ส่งชุดว่างจะไปล้างบัฟจริงของผู้เล่น (อาหาร/ไฟ/สภาพอากาศ) ทิ้งหมด
        // เราไม่มีบัฟเผ่า ⇒ ไม่ต้องส่งอะไร ชุดบัฟที่ถูกต้องถูกส่งอยู่แล้วโดย SendStatusEffects()
        // (server/Core/Player.cs:681) ⇒ รับไว้เฉย ๆ กัน log
        _connection.Recv(delegate(RequestClanStatusEffects msg, PacketHeader header)
        {
        });

        // ── กลุ่มที่ 4: ช่องแชทเผ่า (มาทางสาย Radiotower — ดูหมายเหตุหัวไฟล์) ──────────

        // ToggleClanNotification (4025) — ผู้เล่นกดเปิด/ปิดแจ้งเตือนแชทช่องเผ่า
        // client/SocialSystem.cs:1224-1232 ToggleClanPush: สลับค่าในเครื่องก่อน แล้วส่งทั้งแมป
        // มาให้เซิร์ฟจำ **ไม่รอคำตอบ** ⇒ ห้ามตอบอะไรกลับ แค่จำไว้
        _connection.Recv(delegate(ToggleClanNotification msg, PacketHeader header)
        {
            _clanChannelNotifications = msg.ChannelNotificationsEnabled;
            // [7 ก.ย. 2026] เซฟลงไฟล์ผู้เล่นด้วย — เดิมอยู่แต่ในหน่วยความจำ ออกเกมแล้วค่าหาย
            var saved = new Dictionary<int, bool>();
            if (msg.ChannelNotificationsEnabled != null)
            {
                foreach (var pair in msg.ChannelNotificationsEnabled)
                {
                    saved[(int)pair.Key] = pair.Value;
                }
            }
            _context.ClanChannelNotifications = saved;
            OnContextChanged();
        });

        // GetClanNotificationEnabled (4027) — ถามค่าที่จำไว้ ตอนต่อห้องแชทติดใหม่
        // client/SocialSystem.cs:395-398 (ในสาย ConnectionHelper_Ready) รอ **.On<ToggleClanNotification>**
        // แล้วเอา msg.ChannelNotificationsEnabled ไปวางทับ _clanChannelPushEnabled ตรง ๆ
        // ⇒ ต้องตอบด้วยชนิด ToggleClanNotification (ไม่ใช่ OK) และ **ห้ามส่งแมปเป็น null**
        //    เพราะ SocialSystem.cs:1227 และ :1243 เรียก .Get(key, false) บนตัวนั้นทันที
        //    (ค่าตั้งต้นฝั่งเกมเป็น Dictionary ว่างอยู่แล้ว — SocialSystem.cs:179 — ถ้าเราส่ง null ไปทับจะพัง)
        _connection.Recv(delegate(GetClanNotificationEnabled msg, PacketHeader header)
        {
            // เพิ่งต่อเข้ามา — กู้ค่าจากไฟล์เซฟก่อนตอบ
            if (_clanChannelNotifications == null && _context.ClanChannelNotifications is { Count: > 0 })
            {
                var restored = new Dictionary<ChannelType, bool>();
                foreach (var pair in _context.ClanChannelNotifications)
                {
                    restored[(ChannelType)pair.Key] = pair.Value;
                }
                _clanChannelNotifications = restored;
            }
            Send(new ToggleClanNotification
            {
                ChannelNotificationsEnabled = _clanChannelNotifications ?? new Dictionary<ChannelType, bool>()
            }, header.Seq);
        });

        // ResubscribeClanChannel (24) — สมัครช่องแชทเผ่าใหม่หลังเปลี่ยนเผ่า
        // client/SocialSystem.cs:1334-1343 ClanChanged: หน่วง 10 วินาทีแล้วยิง **ไม่รอคำตอบ**
        // เราไม่มีระบบช่องแชทแยกตามเผ่า (SayInExclusiveChannel ของเรากระจายทั้งโลก —
        // server/Core/Player.cs:438-448) ⇒ รับไว้เฉย ๆ กัน log "ไม่มี handler"
        _connection.Recv(delegate(ResubscribeClanChannel msg, PacketHeader header)
        {
        });
    }
}
