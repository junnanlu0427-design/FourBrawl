using System;
using System.Collections.Generic;
using static Brawl.Core.Defs;

namespace Brawl.Core
{
    public sealed partial class Sim
    {
        public const float DT = 1f / 60f;

        PInput InputFor(Player p, float dt)
        {
            if (State != GameState.Play) return default;
            if (p.Kind == SlotMode.Cpu) return Think(p, dt);
            return Inputs[p.Slot];
        }

        /// <summary>固定步长推进一帧（1/60 秒）。</summary>
        public void Step(float dt = DT)
        {
            if (State == GameState.Paused || State == GameState.Menu) return;
            if (HitStop > 0) { HitStop -= dt; return; }
            if (SlowT > 0) { SlowT -= dt; dt *= .3f; }
            if (State == GameState.Countdown)
            {
                float before = Timer; Timer -= dt;
                if (before > .55f * 3 && Timer <= .55f * 3 || before > 1.1f && Timer <= 1.1f || before > .55f && Timer <= .55f) Sound(Sfx.Beep);
                if (Timer <= 0) { State = GameState.Play; Banner = "开打!"; Timer = .7f; Sound(Sfx.BeepGo); }
            }
            // 超频（ccld 成神时）：5 秒内 ccld 正常速度，其他一切变成 30% 速度
            bool stopped = TimeStopT > 0; float wdt = stopped ? dt * SLOW_K : dt;
            if (stopped) { TimeStopT -= dt; if (TimeStopT <= 0) { TimeStopper = null; Text(W / 2, 200, "超频结束", 0xdfe4ff, true); Shake(8); } }
            foreach (var p in Players) UpdatePlayer(p, p == TimeStopper ? dt : wdt);
            UpdateProjs(dt, stopped);
            dt = wdt;
            {
                UpdateClones(dt); UpdateGadgets(dt); UpdateV9(dt); UpdateTentacles(dt); UpdateHoles(dt); UpdateItems(dt);
                if (State == GameState.Play)
                {
                    RoundT += dt;
                    float lavaAt = Mode == GameMode.Crown ? CROWN_LAVA_AT : LAVA_AT, lavaTop = H * (Mode == GameMode.Crown ? .625f : .46f); // 按地图高度换算
                    bool sudden = Mode != GameMode.Crown && RoundT > SUDDEN_AT; // 突然死亡（借鉴吃鸡缩圈）：120 秒后岩浆一直涨到顶
                    if (sudden) lavaTop = 0;
                    if (RoundT > lavaAt) { if (LavaY > H) LavaY = H; LavaY = Math.Max(lavaTop, LavaY - (sudden ? 22 : 30) * dt); }
                    if (Mode != GameMode.Crown && RoundT > SUDDEN_AT && RoundT - dt <= SUDDEN_AT) { Text(W / 2, 230, "突 然 死 亡 ! 岩浆涨到顶!", 0xff4a2c, true); Shake(10); }
                    if (RoundT > lavaAt - 3 && RoundT - dt <= lavaAt - 3) Text(W / 2, 230, "岩浆要涨上来了!", 0xff7a2c, true);
                }
                UpdateExtras(dt); UpdateRespawns(dt);
            }
            if (State == GameState.Play)
            {
                if (Banner != "") { Timer -= dt; if (Timer <= 0) Banner = ""; }
                if (Mode == GameMode.Crown)
                {
                    if (RoundT > 150 && State == GameState.Play) { Player best = null; foreach (var p in Players) if (best == null || p.Score > best.Score) best = p; if (best != null) best.Score = CROWN_GOAL; } // 超时：分数最高者赢
                    foreach (var p in Players) if (p.Score >= CROWN_GOAL && State == GameState.Play)
                        { State = GameState.RoundEnd; Timer = 2.6f; WinnerSlot = p.Slot; Wins[p.Slot]++; SlowT = 1f; Flash(.3f); Text(W / 2, 250, "王 冠 归 位 !", 0xffd84a, true); }
                }
                else if (TeamsAlive() <= 1)
                {
                    State = GameState.RoundEnd; Timer = 2.6f; WinnerSlot = -1; WinnerTeam = -1;
                    foreach (var p in Players) if (p.Alive) WinnerSlot = p.Slot;
                    if (WinnerSlot >= 0)
                    {
                        if (Mode == GameMode.Team) { WinnerTeam = WinnerSlot % 2; foreach (var p in Players) if (p.Slot % 2 == WinnerTeam) Wins[p.Slot]++; } // 团队：全队一起记胜局
                        else Wins[WinnerSlot]++;
                    }
                }
            }
            else if (State == GameState.RoundEnd)
            {
                Timer -= dt;
                if (Timer <= 0) { if (WinnerSlot >= 0 && Wins[WinnerSlot] >= WIN_ROUNDS) State = GameState.MatchEnd; else StartRound(); }
            }
        }

        void UpdatePlayer(Player p, float dt)
        {
            if (!p.Alive) return;
            p.AtkCd -= dt; p.SkCd -= dt; p.Flash -= dt; p.Shield -= dt; p.Swing -= dt; p.Stun -= dt; p.SpdT -= dt; p.PowT -= dt; p.Daze -= dt; p.SlowT -= dt; p.CloneT -= dt; p.Armor -= dt; p.WallCheck -= dt; p.Bump -= dt;
            p.Coyote -= dt; p.JumpBuf -= dt; p.GiantT -= dt; p.Bubble -= dt; p.SpringT -= dt; p.FrozenT -= dt;
            if (!p.Slam) p.Inv -= dt; p.Hue = (p.Hue + dt * 90) % 360;
            {   // 免控保护：一次控制（眩晕/僵直/冰冻）结束后 1 秒内不能再被控
                bool cc = p.Stun > 0 || p.Daze > 0 || p.FrozenT > 0;
                if (cc && !p.WasCc && p.CcImmune > 0) { p.Stun = 0; p.Daze = 0; p.FrozenT = 0; cc = false; }
                if (p.WasCc && !cc) p.CcImmune = 1;
                p.WasCc = cc; p.CcImmune -= dt;
            }
            if (LavaY < H && p.Y + p.H > LavaY + 6)
            {
                p.Hp -= 45 * dt; p.Vy = Math.Min(p.Vy, -520); p.Stun = .2f; p.OnGround = false;
                if (Rng.NextDouble() < .5) Burst(p.Cx, p.Y + p.H, 0xff7a2c, 4, 160, 5);
                if (p.Hp <= 0) { Ko(p, "熔化!"); return; }
            }
            if (p.Ch == CharId.CCLD) { if (p.God) p.Hp = Math.Min(p.MaxHp, p.Hp + 1.2f * dt); else if (State == GameState.Play) p.Energy = Math.Min(100, p.Energy + 2.4f * LoneK(p) * dt); }
            if (p.Ch == CharId.CCLD && p.God && State == GameState.Play)
            {   // 星环：身边 3 颗星辰，每 0.6 秒灼烧 90 像素内的敌人
                p.StarT -= dt;
                if (p.StarT <= 0)
                {
                    p.StarT = .6f;
                    foreach (var e in new List<Player>(Enemies(p))) if (Hypot(e.Cx - p.Cx, e.Cy - p.Cy) < 90) { Damage(e, 3, p, 0, 0); Zap(p.Cx, p.Cy, e.Cx, e.Cy, 0xfff2a8, 1); }
                }
            }
            p.LatchCd -= dt;
            if (p.Ch == CharId.KAI) { p.ComboT -= dt; if (p.Stk > 0) { p.StkT -= dt; if (p.StkT <= 0) p.Stk = 0; } }
            if (p.Ch == CharId.DVD && p.Stk > 0) { p.StkT -= dt; if (p.StkT <= 0) { p.Stk--; p.StkT = 2f; } }
            if (p.BleedT > 0) { p.BleedT -= dt; Hurt(p, p.BleedDps * dt, p.BleedSrc); if (!p.Alive) return; }
            if (p.Dots > 0) { p.DotT -= dt; if (p.DotOwner != null) p.DotMark = p.DotOwner; Hurt(p, p.Dots * 2.6f * dt, p.DotOwner); if (!p.Alive) return; if (p.DotT <= 0) p.Dots = 0; }
            if (p.BurnT > 0) { p.BurnT -= dt; if (p.BurnSrc != null) p.DotMark = p.BurnSrc; Hurt(p, 2.4f * dt, p.BurnSrc); if (!p.Alive) return; } // 侵蚀燃烧 3 秒
            if (p.Ch == CharId.BW)
            {
                SizeTo(p, (1 + Math.Min(p.Mass, 25) * .03f) * (p.GiantT > 0 ? 1.45f : 1));
                if (p.Absorbs.Count > 0 && p.Shield <= 0)
                {
                    int n = 0;
                    foreach (var a in p.Absorbs)
                    {
                        var o = a.Owner; if (o == null || !o.Alive || o == p) continue; float ang = Rnd(0, 6.283f);
                        float sx = Clamp(o.Cx + (float)Math.Cos(ang) * 200, 20, W - 20), sy = Clamp(o.Cy + (float)Math.Sin(ang) * 200, 20, H - 20);
                        float dx = o.Cx - sx, dy = o.Cy - sy, l = Hypot(dx, dy); if (l < 1) l = 1; float sp = Math.Max(460, a.Speed);
                        Projs.Add(new Proj { X = sx, Y = sy, Vx = dx / l * sp, Vy = dy / l * sp, R = Math.Max(7, Math.Min(15, a.R * 1.2f)) * BwScale(p), Dmg = (float)Math.Round(a.Dmg * 1.2f), Owner = p, Kind = a.Kind == ProjKind.Bomb || a.Kind == ProjKind.IceBomb ? a.Kind : ProjKind.Bolt, Life = 1.4f, Color = 0x3a0a5e });
                        Ring(sx, sy, 4, 44, 0x2a0a40, .35f, 8); Burst(sx, sy, 0x3a0a5e, 10, 220, 5); n++;
                    }
                    if (p.Absorbs.Count > 0) { Text(p.Cx, p.Y - 36, "吐出 " + p.Absorbs.Count + " 发!", 0xc77dff, false); Sound(Sfx.Absorb, 1); }
                    p.Absorbs.Clear();
                }
            }
            if (p.Ch == CharId.DZY) SizeTo(p, (1 + Math.Min(p.Stk, 12) * .035f) * (p.GiantT > 0 ? 1.45f : 1));
            else if (p.Ch != CharId.BW) SizeTo(p, p.GiantT > 0 ? 1.45f : 1);
            var inp = InputFor(p, dt); if (p.Daze > 0 || p.FrozenT > 0) inp = default;
            if (PreMoveNew(p, ref inp, dt)) return;
            if (p.Latch != null)
            {
                var L = p.Latch; L.T -= dt; L.Tick -= dt;
                if (L.T <= 0 || !L.Owner.Alive) { p.Latch = null; p.LatchCd = .8f; }
                else
                {
                    if (L.Tick <= 0) { L.Tick = .6f; AddDot(p, L.Owner, 1); }
                    if (!p.Alive) return;
                    // 黑洞效果是减速 + 往吸点拉，人自己能走出去；离吸点 130 像素就脱离
                    float ldx = L.X - p.Cx, ldy = L.Y - p.Cy, ld = Hypot(ldx, ldy);
                    if (ld > 150) { p.Latch = null; p.LatchCd = .8f; Text(p.Cx, p.Y - 30, "挣脱!", 0xe9c8ff, false); Burst(p.Cx, p.Cy, 0xc77dff, 10, 220, 4); }
                    else if (ld > 1) { p.Vx += ldx / ld * 1300 * dt; p.Vy += ldy / ld * 1000 * dt; }
                    L.Owner.Mass += dt * (Mode == GameMode.Crown ? .25f : .5f); if (!(Mode == GameMode.Crown && CrownHolder == L.Owner.Slot)) L.Owner.BwShield = Math.Min(Mode == GameMode.Crown ? 40 : 70, L.Owner.BwShield + dt * 8);
                }
            }
            float vxb = p.Vx;
            if (p.Grap && p.Latch == null)
            {
                p.GrapT -= dt; float gdx = p.GrapX - p.Cx, gdy = p.GrapY - p.Cy, gd = Hypot(gdx, gdy); if (gd < 1) gd = 1;
                if (gd < 34 || p.GrapT <= 0) { p.Grap = false; p.Vy = Math.Min(p.Vy, -380); p.Jumps = 1; }
                else
                {
                    p.Vx = gdx / gd * 1150; p.Vy = gdy / gd * 1150;
                    var gh = MoveBox(ref p.X, ref p.Y, p.W, p.H, p.Vx * dt, p.Vy * dt);
                    if (gh.Hx || gh.Hy) { p.Grap = false; p.Vy = -380; p.Jumps = 1; }
                    p.OnGround = false;
                }
            }
            else
            {
                float spd = (p.God ? 400 : p.C.Speed) * (p == TimeStopper && TimeStopT > 0 ? 1.5f : 1) * SPD_MULT * (p.SpdT > 0 ? 1.4f : 1) * (p.SlowT > 0 ? .6f : 1) * (p.Latch != null ? .5f : 1) * (Mode == GameMode.Crown && CrownHolder == p.Slot ? .9f : 1) * SpeedMulNew(p);
                int ix = (inp.R ? 1 : 0) - (inp.L ? 1 : 0);
                if (p.BallT > 0 && ix == 0) ix = p.Vx >= 0 ? 1 : -1; // 滚球不会停
                if (ix != 0 && !(p.Bump > 0)) p.Face = ix;
                if (p.Bump > 0)
                {
                    p.Vx = p.Face * 720;
                    foreach (var e in new List<Player>(Enemies(p)))
                        if (!p.BumpHit.Contains(e) && Overlap(p.X - 6, p.Y, p.W + 12, p.H, e.X, e.Y, e.W, e.H))
                        {
                            p.BumpHit.Add(e); Damage(e, (float)Math.Round(13 * (p.RageNow ? 1.5f : 1)), p, p.Face * 650, 240); e.WallCheck = .4f; e.WallBy = p;
                            if (p.RageNow) Text(e.Cx, e.Y - 30, "暴怒顶!", 0x4cff8a, false);
                        }
                }
                else
                {   // 手感：地面起步/刹车更快，空中也能较好地调整方向
                    float acc = p.Stun > 0 ? 2 : (p.OnGround ? (OnIce(p) ? 2.5f : (ix == 0 ? 24 : 20)) : (ix != 0 && Sign(ix) != Sign(p.Vx) ? 13 : 10)); if (p.FrozenT > 0) acc = 1;
                    if (p.WallLock > 0) { p.WallLock -= dt; acc = 2; } // 蹬墙跳后短时间内不被方向键拉回墙上
                    if (!(p.DodgeT > 0) && !(p.SpatT > 0)) p.Vx += (ix * spd - p.Vx) * Math.Min(1, (p.BallT > 0 ? 6 : acc) * dt);
                }

                bool upPressed = inp.U && !p.PrevUp;
                if (upPressed) p.JumpBuf = JUMP_BUFFER;
                if (p.Slam)
                {
                    var t = Nearest(p); if (t != null) p.Vx += (Sign(t.Cx - p.Cx) * 260 - p.Vx) * Math.Min(1, 1.5f * dt);
                    if (p.Vy >= 0 && !p.Hung) { p.Hung = true; p.Hang = .3f; }
                    if (p.Hang > 0) { p.Hang -= dt; p.Vy = 0; } else if (p.Hung) p.Vy = 1350; else p.Vy += GRAV * GravK * LocalGrav(p) * dt;
                }
                else if (p.God) { float iy = (inp.D ? 1 : 0) - (inp.U ? 1 : 0); p.Vy += (iy * 420 - p.Vy) * Math.Min(1, (p.Stun > 0 ? 2 : 8) * dt); }
                else
                {
                    if (!p.OnGround && p.Jumps == 0 && p.Coyote <= 0) p.Jumps = 1;
                    bool canGround = p.OnGround || p.Coyote > 0;
                    int maxJ = (p.SpringT > 0 ? 3 : 2) + (p.Ch == CharId.CCLD2 && p.Divine && p.SpringT <= 0 ? 1 : 0);
                    // 贴墙滑 + 蹬墙跳（空中按着方向键贴住墙：慢慢往下滑；这时按跳 = 朝反方向蹬出去，并恢复一次二段跳）
                    int wdir = !canGround && ix != 0 && p.BallT <= 0 && BoxHit(p.X + ix * 3, p.Y + 4, p.W, p.H - 8) ? ix : 0;
                    p.WallDir = wdir;
                    if (wdir != 0 && upPressed && !(p.Bump > 0))
                    {
                        p.Vy = -p.C.Jump * JUMP_MULT * .95f; p.Vx = -wdir * 520; p.Face = -wdir; p.WallLock = .2f; p.Jumps = 1; p.JumpBuf = 0; p.JumpCut = true; upPressed = false;
                        Burst(wdir > 0 ? p.X + p.W : p.X, p.Cy, 0xc8ccf0, 8, 160, 3); Sound(Sfx.Jump);
                    }
                    else if (p.JumpBuf > 0 && !(p.Bump > 0) && (canGround || (upPressed && p.Jumps < maxJ)))
                    {
                        p.Vy = -p.C.Jump * JUMP_MULT * (p.SpringT > 0 ? 1.3f : 1); p.Jumps = canGround ? 1 : p.Jumps + 1; p.OnGround = false; p.Coyote = 0; p.JumpBuf = 0; p.JumpCut = true;
                        Burst(p.Cx, p.Y + p.H, 0x9097c2, 5, 120, 3); Sound(Sfx.Jump);
                    }
                    // 小跳：松开跳跃键时截断上升；跳到最高点附近重力稍轻，更好控落点
                    if (p.JumpCut && !inp.U && p.Vy < -200) { p.Vy *= .55f; p.JumpCut = false; }
                    if (p.Vy >= 0) p.JumpCut = false;
                    float gk = inp.U && Math.Abs(p.Vy) < 140 ? .65f : 1;
                    p.Vy += GRAV * GravK * LocalGrav(p) * gk * dt * (inp.D && !p.OnGround && p.Ch != CharId.BW ? 1.8f : 1); if (p.Vy > 1300) p.Vy = 1300;
                    if (p.Ch == CharId.CCLD2 && p.Divine && inp.U && p.Vy > 140) p.Vy = 140; // 神化后按住跳滑翔
                    if (p.WallDir != 0 && p.Vy > 170 && !inp.D) p.Vy = 170; // 贴墙滑
                }
                bool wasGround = p.OnGround;
                var h = MoveBox(ref p.X, ref p.Y, p.W, p.H, p.Vx * dt, p.Vy * dt); p.OnGround = false;
                if (h.Hy) { if (p.Vy > 0) { p.OnGround = true; p.Jumps = 0; p.Coyote = COYOTE; if (p.Slam) XpLand(p); } p.Vy = 0; }
                if (p.OnGround && !p.Slam) FootTiles(p, dt);
                if (!p.OnGround && wasGround && p.Vy >= 0) { /* 刚走下平台：土狼时间仍可起跳 */ }
                if (h.Hx && ix != 0 && Sign(p.Vx) == ix && (wasGround || p.OnGround) && !p.Slam && !(p.Bump > 0))
                {   // 自动迈上一格高的小台阶
                    float nx = p.X + ix * 3, ny = p.Y - T - 1;
                    if (!BoxHit(p.X, ny, p.W, p.H) && !BoxHit(nx, ny, p.W, p.H)) { p.X = nx; p.Y = ny; h.Hx = false; }
                }
                if (h.Hx)
                {
                    if (p.WallCheck > 0 && Math.Abs(vxb) > 150)
                    {
                        p.WallCheck = 0; if (!(p.Armor > 0)) p.Daze = .8f; Hurt(p, 6, p.WallBy); Text(p.Cx, p.Y - 30, "撞墙眩晕!", 0xffe27a, false); Shake(8); Sound(Sfx.Heavy, 10);
                    }
                    p.Vx = 0;
                }
                PostMoveNew(p, h, vxb, dt);
                if (p.Y < 0) { p.Y = 0; p.Vy = Math.Max(0, p.Vy); }
                if (p.God) { p.X = Clamp(p.X, 0, W - p.W); p.Y = Math.Min(p.Y, H - p.H - 30); }
            }
            if (inp.Face != 0 && !(p.Bump > 0)) p.Face = inp.Face;
            p.PrevUp = inp.U;
            if (p.Y > H + 60 || p.X < -140 || p.X > W + 140) { FellOff(p); return; }
            if (State == GameState.Play && !p.Slam && !(p.Bump > 0))
            {
                if (inp.A && p.AtkCd <= 0) { Attack(p, inp); p.AtkCd *= ATK_MULT; if (p == TimeStopper && TimeStopT > 0) p.AtkCd *= .6f; }
                if (inp.S && p.SkCd <= 0) { Skill(p, inp); if (p.SkCd > .7f) p.SkCd *= CD_MULT; }
            }
        }

        /// <summary>DVD 光盘反弹：每弹一次更快、更疼；0.35 秒内连弹两次 = “撞到角落”，原地爆一个大的。</summary>
        void DiscBounce(Proj b)
        {
            if (b.Remix) { Disc2Bounce(b); return; }
            b.Vx *= 1.12f; b.Vy *= 1.12f; b.Dmg += 2; b.R = Math.Min(b.R + 1, 22);
            Ring(b.X, b.Y, 4, 26, 0xff4fd8, .2f);
            if (RoundT - b.BounceT < .35f && b.Owner != null)
            {
                var o = b.Owner; Text(b.X, b.Y - 20, "撞角落!", 0xff4fd8, true); Ring(b.X, b.Y, 10, 120, 0xff9be8, .4f, 10); Burst(b.X, b.Y, 0xff4fd8, 30, 420, 6); Shake(8); Sound(Sfx.Boom, 10);
                foreach (var e in new List<Player>(Enemies(o))) { float dx = e.Cx - b.X, dy = e.Cy - b.Y, d = Hypot(dx, dy); if (d < 120) Damage(e, 16, o, Sign(dx == 0 ? 1 : dx) * 420, -320); }
                if (o.Alive) { o.Stk = Math.Min(6, o.Stk + 2); o.StkT = 5; }
                b.BounceT = -9;
            }
            else b.BounceT = RoundT;
        }

        void ProjHit(Proj b, Player p)
        {
            if (ProjHitNew(b, p)) return;
            float l = Hypot(b.Vx, b.Vy); if (l < 1) l = 1; var o = b.Owner;
            if (b.Kind == ProjKind.Hook)
            {
                if (o != null && o.Alive)
                {
                    float dx = o.Cx - p.Cx, dy = o.Cy - p.Cy, d = Hypot(dx, dy); if (d < 1) d = 1;
                    Damage(p, 10, o, 0, 0);
                    if (p.Ch != CharId.XP) { p.Vx = dx / d * Math.Min(1100, d * 4); p.Vy = dy / d * Math.Min(900, d * 3) - 150; p.OnGround = false; }
                    p.Stun = .5f; o.AtkCd = 0; o.Inv = Math.Max(o.Inv, .3f); Text(p.Cx, p.Y - 30, "抓到你了!", 0xff8a3c, false);
                }
                return;
            }
            float dmgMul = 1;
            if (o != null && o.Ch == CharId.GARY && b.Kind == ProjKind.Kunai && p.Face == Sign(b.Vx)) { dmgMul = 1.8f; Text(p.Cx, p.Y - 34, "背刺!", 0xff8a3c, false); }
            float kb = b.Kind == ProjKind.Wave ? 320 : b.Kind == ProjKind.Sword ? 120 : b.Kind == ProjKind.Hole ? 0 : 260;
            Damage(p, (float)Math.Round(b.Dmg * dmgMul), o, b.Vx / l * kb, b.Kind == ProjKind.Hole ? 0 : -180);
            if (b.Slow)
            {
                p.BleedDps = Math.Min(10, (p.BleedT > 0 ? p.BleedDps : 0) + 4); p.BleedT = 4; p.BleedSrc = o;
                if (o != null && o.Ch == CharId.DVD) { o.Stk = Math.Min(6, o.Stk + 1); o.StkT = 5; if (o.Stk == 6) Text(o.Cx, o.Y - 36, "满层! 狂射", 0xff4fd8, true); }
            }
            if (b.Fast && o != null && o.Alive) { o.Hp = Math.Min(o.MaxHp, o.Hp + 2.5f); o.StkT = 5; }
            if (b.Kind == ProjKind.Hole && o != null && p.Alive) { if (p.LatchCd <= 0 && p.Latch == null) p.Latch = new Latch { Owner = o, X = b.X, Y = b.Y, T = 1.1f + Math.Min(o.Mass, 25) * .01f, Tick = .5f }; AddDot(p, o, 1); }
        }

        void UpdateProjs(float dt, bool stopped)
        {
            foreach (var b in Projs.ToArray())
            {
                float bdt = stopped && b.Owner != TimeStopper ? dt * SLOW_K : dt;
                if (b.Home > 0 && b.Owner != null)
                {   // 追踪弹：朝 320 像素内最近的敌人慢慢转弯
                    Player tg = null; float bd = 320; foreach (var e in Players) if (e.Alive && e != b.Owner && !Ally(e, b.Owner) && (b.HitSet == null || !b.HitSet.Contains(e))) { float d = Hypot(e.Cx - b.X, e.Cy - b.Y); if (d < bd) { bd = d; tg = e; } }
                    if (tg != null)
                    {
                        float sp0 = Hypot(b.Vx, b.Vy), a0 = (float)Math.Atan2(b.Vy, b.Vx), a1 = (float)Math.Atan2(tg.Cy - b.Y, tg.Cx - b.X), da = a1 - a0;
                        while (da > Math.PI) da -= 2 * (float)Math.PI; while (da < -Math.PI) da += 2 * (float)Math.PI;
                        float turn = Clamp(da, -b.Home * bdt, b.Home * bdt); a0 += turn; b.Vx = (float)Math.Cos(a0) * sp0; b.Vy = (float)Math.Sin(a0) * sp0;
                    }
                }
                b.Life -= bdt; if (b.Kind == ProjKind.Bomb || b.Kind == ProjKind.IceBomb || b.Kind == ProjKind.Spore) b.Vy += 1100 * GravK * bdt;
                int n = Math.Max(1, (int)Math.Ceiling(Hypot(b.Vx, b.Vy) * bdt / 6));
                for (int s = 0; s < n && b.Life > 0; s++)
                {
                    float px = b.X, py = b.Y; b.X += b.Vx * bdt / n; b.Y += b.Vy * bdt / n;
                    if (b.Kind == ProjKind.Sword) { if (b.Y > H + 20) { b.Life = 0; continue; } }
                    else if (b.X < 0 || b.X > W || b.Y < 0)
                    {
                        if (b.Bounces > 0) { b.Bounces--; if (b.X < 0 || b.X > W) b.Vx *= -1; else b.Vy *= -1; b.X = px; b.Y = py; if (b.Kind == ProjKind.Disc) DiscBounce(b); } else b.Life = 0;
                        continue;
                    }
                    if (b.Y > H + 20) { b.Life = 0; continue; }
                    int c = (int)Math.Floor(b.X / T), r = (int)Math.Floor(b.Y / T);
                    if (b.Kind != ProjKind.Wave && Solid(c, r))
                    {
                        if (ProjWallNew(b, px, py, c, r)) continue;
                        if (b.Kind == ProjKind.Hook)
                        {
                            if (b.Owner != null && b.Owner.Alive) { b.Owner.Grap = true; b.Owner.GrapX = px; b.Owner.GrapY = py; b.Owner.GrapT = .7f; b.Owner.Inv = Math.Max(b.Owner.Inv, .45f); Ring(px, py, 4, 26, 0xffd27a, .25f); Sound(Sfx.Hit, 4); }
                            b.Life = 0; continue;
                        }
                        if (b.Kind == ProjKind.Sword) { DestroyAt(b.X, b.Y, b.Dig); Burst(b.X, b.Y, 0xe0f4ff, 6, 200, 3); b.Life = 0; continue; }
                        if (b.Bounces > 0)
                        {
                            b.Bounces--; int co = (int)Math.Floor(px / T), ro = (int)Math.Floor(py / T);
                            if (c != co && Solid(c, ro)) b.Vx *= -1; else if (r != ro && Solid(co, r)) b.Vy *= -1; else { b.Vx *= -1; b.Vy *= -1; b.BounceT2 = RoundT; }
                            DestroyAt(c * T + T / 2, r * T + T / 2, 6); b.X = px; b.Y = py;
                            if (b.Kind == ProjKind.Disc) DiscBounce(b);
                        }
                        else { if (b.Kind != ProjKind.Bomb && b.Kind != ProjKind.IceBomb) { DestroyAt(b.X, b.Y, b.Dig); Burst(b.X, b.Y, b.Color, 5, 160, 3); } b.Life = 0; }
                        continue;
                    }
                    foreach (var p in Players)
                    {
                        if (!p.Alive || p == b.Owner || b.Life <= 0 || p.Inv > 0 || Ally(p, b.Owner)) continue; // 团队模式：子弹穿过队友
                        float hx = (b.Kind == ProjKind.Sword ? 8 : b.R) + p.W / 2, hy = (b.Kind == ProjKind.Wave ? b.Hh : b.Kind == ProjKind.Sword ? 26 : b.R) + p.H / 2;
                        if (Math.Abs(b.X - p.Cx) < hx && Math.Abs(b.Y - p.Cy) < hy)
                        {
                            if (!b.Pierce && b.HitSet != null && b.HitSet.Contains(p)) continue;
                            if (p.Hidden) continue;
                            if (ProjBlockNew(b, p)) { b.Life = 0; continue; }
                            if (p.Ch == CharId.BW && p.Shield > 0 && !(b.Owner != null && b.Owner.Ch == CharId.CCLD)) { p.Absorbs.Add(new Absorb { Kind = b.Kind, Dmg = b.Dmg, Speed = Hypot(b.Vx, b.Vy), R = b.R, Owner = b.Owner }); b.Life = 0; Ring(p.Cx, p.Cy, p.H, 6, 0xc77dff, .2f); continue; }
                            if (b.Kind == ProjKind.Bomb || b.Kind == ProjKind.IceBomb) { b.Life = 0; continue; }
                            if (b.Pierce) { if (b.HitSet.Contains(p)) continue; b.HitSet.Add(p); ProjHit(b, p); continue; }
                            ProjHit(b, p); b.Life = 0;
                        }
                    }
                }
            }
            foreach (var b in Projs.ToArray()) if ((b.Kind == ProjKind.Bomb || b.Kind == ProjKind.IceBomb) && b.Life <= 0 && !b.Boomed) { b.Boomed = true; if (b.Y < H) { if (b.Kind == ProjKind.Bomb) Explode(b.X, b.Y, b.Owner); else IceBurst(b.X, b.Y, b.Owner); } }
            ProjCleanupNew();
            Projs.RemoveAll(b => b.Life <= 0);
        }

        void UpdateClones(float dt)
        {
            foreach (var c in Clones.ToArray())
            {
                if (c.T < 0) continue; c.T -= dt; var o = c.Owner;
                if (c.Kind != 0) { UpdateCloneNew(c, dt); continue; }
                Player t = null; if (c.Tgt >= 0) foreach (var q in Players) if (q.Slot == c.Tgt && q.Alive) t = q;
                if (t == null) t = Nearest(o, c.Cx, c.Cy);
                int dir = t != null ? Sign(t.Cx - c.Cx) : 0; if (dir != 0) c.Face = dir;
                c.Vx += (dir * 340 - c.Vx) * Math.Min(1, 10 * dt); c.Vy += GRAV * GravK * dt; if (c.Vy > 1300) c.Vy = 1300;
                if (c.OnGround && t != null && (t.Cy < c.Y - 40 || SolidPx(c.Cx + dir * (c.W / 2 + 6), c.Y + c.H - 10))) { c.Vy = -760; c.OnGround = false; }
                var h = MoveBox(ref c.X, ref c.Y, c.W, c.H, c.Vx * dt, c.Vy * dt); c.OnGround = false;
                if (h.Hy) { if (c.Vy > 0) c.OnGround = true; c.Vy = 0; }
                if (h.Hx) c.Vx = 0;
                Player hitP = null; foreach (var q in Players) if (q != o && q.Alive && !Ally(q, o) && Overlap(c.X, c.Y, c.W, c.H, q.X, q.Y, q.W, q.H)) hitP = q;
                if (hitP != null) CloneBoom(c); else if (c.T <= 0 || c.Y > H) CloneBoom(c);
            }
            Clones.RemoveAll(c => c.T <= 0);
        }

        void UpdateHoles(float dt)
        {
            foreach (var h in Holes.ToArray())
            {
                h.T += dt; if (h.T < h.Delay) continue;
                float k = Clamp((h.T - h.Delay) / (h.Dur * .6f), 0, 1); h.R = 24 + (h.Rmax - 24) * k;
                foreach (var p in Players.ToArray())
                {
                    if (!p.Alive || p.Inv > 0 || Ally(p, h.Owner)) continue; float dx = h.X - p.Cx, dy = h.Y - p.Cy, d = Hypot(dx, dy); if (d < 1) d = 1;
                    if (d < h.R) { float s = (1 - d / h.R) * 1300 + 250; p.Vx += dx / d * s * dt * 2.2f; p.Vy += dy / d * s * dt * 2.2f; p.OnGround = false; if (d < 36 + h.R * .08f) Hurt(p, 40 * dt, h.Owner); }
                }
                if (h.T > h.Delay + h.Dur) { h.Done = true; Ring(h.X, h.Y, h.R, 10, 0xc77dff, .4f, 10); Flash(.3f); Shake(14); Sound(Sfx.Boom, 14); }
            }
            Holes.RemoveAll(h => h.Done);
        }

        void UpdateItems(float dt)
        {
            if (State == GameState.Play && RoundT >= 30 && RoundT - dt < 30 && Mode != GameMode.Crown) SpawnUltBall();
            if (State == GameState.Play) { ItemTimer -= dt; if (ItemTimer <= 0 && ItemsList.Count < (Map.Big ? 5 : 3)) { SpawnItem(); ItemTimer = Rnd(6, 10) / (Map.Big ? 1.6f : 1); } }
            foreach (var it in ItemsList)
            {
                it.Life -= dt;
                if (it.Type == Items.Ult) { it.Life = Math.Max(it.Life, .1f); it.TpCd += dt; it.X = Clamp(it.X + (float)Math.Sin(it.TpCd * .9f) * 140 * dt, 40, W - 70); it.Y = Clamp(it.Y + (float)Math.Cos(it.TpCd * 1.3f) * 90 * dt + (it.Y < H * .25f ? 60 * dt : 0), 40, H * .6f); }
                else if (!it.Landed) { it.Vy = Math.Min(it.Vy + 500 * dt, 160); var h = MoveBox(ref it.X, ref it.Y, it.W, it.H, 0, it.Vy * dt); if (h.Hy && it.Vy > 0) { it.Landed = true; it.Vy = 0; } }
                else { it.Vy += GRAV * GravK * dt; var h = MoveBox(ref it.X, ref it.Y, it.W, it.H, 0, it.Vy * dt); if (h.Hy) it.Vy = 0; }
                if (it.Y > LavaY || it.Y > H) it.Life = 0;
                foreach (var p in Players) if (p.Alive && it.Life > 0 && Overlap(p.X, p.Y, p.W, p.H, it.X, it.Y, it.W, it.H)) { PickItem(p, it); it.Life = 0; }
            }
            ItemsList.RemoveAll(it => it.Life <= 0);
        }
        void PickItem(Player p, Item it)
        {
            Text(p.Cx, p.Y - 30, "+" + Items.Names[it.Type], Items.Colors[it.Type], true); Ring(p.Cx, p.Cy, 10, 60, Items.Colors[it.Type], .3f); Sound(Sfx.Pickup);
            switch (it.Type)
            {
                case 0: p.Bomb = true; break;
                case 1: p.Hp = Math.Min(p.MaxHp, p.Hp + 35); break;
                case 2: p.SpdT = 7; break;
                case 3: p.PowT = 8; break;
                case 4: p.GiantT = 8; break;
                case 5: p.IceBomb = true; break;
                case 6: p.Bubble = 10; break;
                case 7: p.SpringT = 10; break;
                case Items.Ult: p.Ult = true; Text(p.Cx, p.Y - 56, "终极就绪! 按技能", 0xfff2a8, true); Flash(.2f); break;
                default: if (TimeStopT > 0) p.Hp = Math.Min(p.MaxHp, p.Hp + 35); else DiceSwap(); break;
            }
        }

        // ================= 电脑 AI（与网页版同逻辑） =================
        PInput Think(Player p, float dt)
        {
            var ai = p.Ai; var t = Nearest(p); if (t == null) return default;
            if (Mode == GameMode.Crown && CrownHolder >= 0 && CrownHolder != p.Slot) foreach (var q in Players) if (q.Slot == CrownHolder && q.Alive && Hypot(q.Cx - p.Cx, q.Cy - p.Cy) < 700) t = q;
            float dx = t.Cx - p.Cx, dy = t.Cy - p.Cy, ad = Math.Abs(dx); int sd = Sign(dx); if (sd == 0) sd = 1;
            if (p.BlindT > 0) { ai.T -= dt; if (ai.T <= 0) { ai.T = Rnd(.2f, .5f); ai.Jit = Rng.Next(3) - 1; } return new PInput { L = ai.Jit < 0, R = ai.Jit > 0 }; } // 被致盲：乱走
            bool ranged = IsNew(p.Ch) ? RangedNew(p.Ch) : ((p.Ch == CharId.DZY && p.Stk == 0) || (p.Ch == CharId.KAI && ad > 60) || (p.Ch == CharId.GARY && ad > 120) || p.Ch == CharId.DVD || (p.Ch == CharId.BW && ad > 130) || (p.Ch == CharId.CCLD && p.God));
            float pref = IsNew(p.Ch) ? PrefNew(p) : p.Ch == CharId.DZY ? (p.Stk > 0 ? 90 : 150) : p.Ch == CharId.KAI ? 140 : p.Ch == CharId.DVD ? 300 : p.Ch == CharId.BW ? (ad > 130 ? 260 : 20) : ranged ? 260 : 20;
            ai.T -= dt; if (ai.T <= 0) { ai.T = Rnd(.4f, 1.2f); ai.Jit = Rng.NextDouble() < .25 ? (Rng.NextDouble() < .5 ? -1 : 1) : 0; }
            int dir = 0; if (ad > pref + 20) dir = sd; else if (ranged && ad < pref - 110) dir = -sd;
            if (ai.Jit != 0 && ad < pref + 60) dir = ai.Jit;
            if (p.Ch == CharId.DZY && p.Stk == 0 && ad < 260) { Tentacle home = null; float hd = 1e9f; foreach (var tc in Tentacles) if (tc.Owner == p) { float d = Math.Abs(tc.X - p.Cx); if (d < hd) { hd = d; home = tc; } } if (home != null && hd > 40) dir = Sign(home.X - p.Cx); }
            bool u = false; float foot = p.Y + p.H + 2;
            if (p.OnGround && dir != 0)
            {
                float axp = p.Cx + dir * (p.W / 2 + 14);
                if (!GroundBelow(axp, foot)) { if (dy < -40 || GroundBelow(p.Cx + dir * 150, foot - 60)) u = true; else dir = 0; }
            }
            if (!p.OnGround && !GroundBelow(p.Cx, p.Y + p.H)) { dir = Sign(W / 2 - p.Cx); if (p.Vy > 40 && (p.Jumps < 2 || p.God)) u = true; }
            if (p.OnGround && dy < -70 && ad < 300 && Rng.NextDouble() < .1) u = true;
            if (p.Ch == CharId.KAI && dy < -40 && ad < 220 && (p.OnGround || (p.Vy > 0 && p.Jumps > 0)) && Rng.NextDouble() < .25) u = true;
            if (dir != 0 && SolidPx(p.Cx + dir * (p.W / 2 + 6), p.Y + p.H - 12)) u = true;
            Item near = null; foreach (var it in ItemsList) if (Math.Abs(it.X + 13 - p.Cx) < 280 && Math.Abs(it.Y - p.Y) < 140) { near = it; break; }
            if (Mode == GameMode.Crown && CrownHolder < 0 && CrownY > 0) { int cd = Sign(CrownX - p.Cx); if (Math.Abs(CrownX - p.Cx) > 10) dir = cd; if (CrownY < p.Y - 50 && p.OnGround) u = true; }
            if (near != null && ad > 110) { int nd = Sign(near.X + 13 - p.Cx); if (nd != 0) dir = nd; if (near.Y < p.Y - 40 && p.OnGround) u = true; }
            foreach (var h in Holes) { float d = Hypot(h.X - p.Cx, h.Y - p.Cy); if (h.T > h.Delay && d < h.R + 60) { dir = p.Cx < h.X ? -1 : 1; u = true; } }
            if (LavaY < H && p.Y + p.H > LavaY - 110 && p.Vy >= -50) u = true;
            if (ai.JumpHold > 0) { ai.JumpHold -= dt; u = true; } else { if (u && p.PrevUp) u = false; if (u && p.OnGround) ai.JumpHold = .2f; }
            bool dd = false;
            if (p.God) { float off = p.Cy - (t.Cy - 110); u = off > 25; dd = off < -25; if (ad > pref + 20) dir = sd; else if (ad < pref - 110) dir = -sd; else dir = ai.Jit; }
            bool a = false, s = false;
            if (p.Bomb) { if (ad < 380 && Math.Abs(dy) < 200) a = true; }
            else if (p.Ch == CharId.BW) { if (ad < 120 && Math.Abs(dy) < 80) { a = true; dd = true; } else if (ad < 520 && Math.Abs(dy) < 120) a = true; }
            else if (p.Ch == CharId.XP) { if (ad < 150 && Math.Abs(dy) < 40) a = true; }
            else if (p.Ch == CharId.DZY) { if (p.Stk > 0 ? (ad < 150 + p.Stk * 8 && Math.Abs(dy) < 90) || (ad < 500 && p.WhipN % 4 == 3) : (ad < 600 && p.OnGround)) a = true; }
            else if (ranged) { if (Math.Abs(dy) < (p.God ? 320 : 90) && ad < (p.Ch == CharId.KAI ? 230 : p.Ch == CharId.DVD && p.Stk < 6 ? 520 : 700)) a = true; }
            else if (ad < (p.W + t.W) / 2 + (p.Ch == CharId.KAI ? 200 : 42) && Math.Abs(dy) < (p.Ch == CharId.KAI ? 80 : 50)) a = true;
            if (p.SkCd <= 0)
            {
                if (p.Ch == CharId.DVD && p.Stk >= DVD_ULT && ad < 600) s = true;
                if (p.Ch == CharId.XP && ad < 300 && ad > 40 && Math.Abs(dy) < 160 && p.OnGround) s = true;
                if (p.Ch == CharId.BW) foreach (var b in Projs) if (b.Owner != p && Hypot(b.X - p.Cx, b.Y - p.Cy) < 140) { s = true; break; }
                if (p.Ch == CharId.DZY) { int nt = 0; foreach (var tc in Tentacles) if (tc.Owner == p) nt++; if (nt >= (p.Stk == 0 ? 3 : 2) || (nt >= 2 && p.Hp < p.MaxHp * .35f)) s = true; }
                if (p.Ch == CharId.GARY && ad > 220 && ad < 620 && Math.Abs(dy) < 200) s = true;
                if (p.Ch == CharId.KAI && ((ad > 90 && ad < 340 && Math.Abs(dy) < 240) || (Math.Abs(dy) > 70 && ad < 200 && Math.Abs(dy) < 260) || (p.Stk >= 5 && ad < 260 && Math.Abs(dy) < 160))) s = true;
                if (p.Ch == CharId.CCLD && (p.God ? Rng.NextDouble() < .03 : p.Energy >= 100 || (ad > 120 && ad < 300 && Math.Abs(dy) < 50 && p.Face == Sign(t.Cx - p.Cx)))) s = true;
            }
            if (IsNew(p.Ch)) ThinkNew(p, t, ad, dy, ref a, ref s, ref dd, ref dir);
            ThinkV9(p, t, ad, dy, ref a, ref s, ref dd);
            return new PInput { L = dir < 0, R = dir > 0, U = u, D = dd, A = a, S = s, Face = (a || s) ? sd : 0 };
        }
    }
}
