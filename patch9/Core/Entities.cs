using System.Collections.Generic;

namespace Brawl.Core
{
    public struct PInput
    {
        public bool L, R, U, D, A, S; public int Face;
        public byte Pack() => (byte)((L ? 1 : 0) | (R ? 2 : 0) | (U ? 4 : 0) | (D ? 8 : 0) | (A ? 16 : 0) | (S ? 32 : 0));
        public static PInput Unpack(byte m) => new PInput { L = (m & 1) != 0, R = (m & 2) != 0, U = (m & 4) != 0, D = (m & 8) != 0, A = (m & 16) != 0, S = (m & 32) != 0 };
    }

    public sealed class Latch { public Player Owner; public float X, Y, T, Tick; }
    public sealed class Absorb { public ProjKind Kind; public float Dmg, Speed, R; public Player Owner; }
    public sealed class AiState { public float T, JumpHold; public int Jit; }

    public sealed class Player
    {
        public int Slot; public CharId Ch; public CharDef C; public SlotMode Kind; public string Label = "";
        public float X, Y, W, H, Vx, Vy, Hp, MaxHp; public int Face = 1;
        public bool OnGround, PrevUp = true, Alive = true; public int Jumps = 1;
        public float AtkCd = .3f, SkCd = 2, Flash, Shield, Swing, Stun, Energy, Inv, SpdT, PowT, StkT, CloneT, BleedT, BleedDps, DotT, Mass, BwShield, Mult = 1, Daze, SlowT, Armor, WallCheck, Bump, Hang, Hue, DmgAcc;
        public bool God, Slam, Hung, Bomb, Rage, RageNow;
        public int Stk, Dots, HitsTaken;
        public Player BleedSrc, DotOwner, WallBy; public Latch Latch;
        public List<Absorb> Absorbs = new List<Absorb>(); public HashSet<Player> BumpHit = new HashSet<Player>();
        public Dictionary<int, float> Hitters = new Dictionary<int, float>();
        public AiState Ai = new AiState();
        public float Coyote, JumpBuf; // 手感
        public float TargetX, TargetY, SkPctNet = -1; // 朋友端用
        public bool Grap; public float GrapX, GrapY, GrapT; // gary 钩锁
        public float GiantT, Bubble, SpringT, FrozenT, TpCd; public bool IceBomb;   // 新道具
        public bool Ghost; public float GhostCd, RespawnT, CrownDmg, Score; public int Bounty; // 幽灵 / 王冠模式
        public int Kills; public float DamageDealt;
        public int ComboN; public float ComboT; // kai 连击
        public float LatchCd; // 挣脱黑洞后的短暂免疫
        public bool JumpCut; // 小跳
        public float StarT; // ccld 创世神星环
        public int WhipN; // dzy 进化后鞭数（Stk = 进化等级）
        public float BurnT; public Player BurnSrc; public Player DotMark; // bw：侵蚀燃烧 / 受过 DOT 的人死后会爆
        public float CcImmune; public bool WasCc; // 控制结束后 1 秒内免控
        // ---- 第六版新角色 ----
        public float LastAtkT = -9, MarkT; public int HookStk; public Player MarkSrc;           // px：蓄力霰弹 / 印记 / 钩子层数
        public float PullT, PullX, PullY; public Player PullBy; public int PullDmg;            // 被钩子 / 收网拉动
        public bool Ult; public float V9Cd, V9SkCd, ButtT, WallLock; public int SwapN, WallDir; // 第九版：下+攻击 / 下+技能 的独立冷却、xp 屁股坐
        public bool Divine; public int Combo3; public float ComboT3, ParryT, ParryMiss, BlindT; public int Day;    // ccld-改
        public int Ammo = 3, AmmoMax = 3;                                                     // DVD-改
        public Player Held, HeldBy, SpatBy; public float HoldT, BallT, SpatT; public float StoredDmg; // xp-改
        public float PushT, BurstCd; public Player PushBy;                                    // bw-改
        public int Pol, SelfPol = 1; public float PolT;                                       // kai-改
        public float SubT, TrailT;                                                            // dzy-改
        public float DodgeT, DodgeCd, TapL = -9, TapR = -9; public bool PrevL, PrevR;         // 通用闪避（双击左右）
        public int Streak; public float StreakT;                                              // 连杀
        public int XFlags; public int XVal;                                                   // 朋友端用：新角色状态
        public bool Hidden => HeldBy != null || SubT > 0;
        public float Cx => X + W / 2; public float Cy => Y + H / 2;
    }

    public enum ProjKind : byte { Bolt, Disc, Hole, Wave, Sword, Bomb, Kunai, Hook, IceBomb, Puff, Pellet, Spore, Push }

    public sealed class Proj
    {
        public float X, Y, Vx, Vy, R, Dmg, Life = 1, Hh, Ang; public Player Owner; public int Bounces, Dig = 12; public uint Color;
        public ProjKind Kind; public bool Pierce, Slow, Fast, Boomed; public HashSet<Player> HitSet; public float TpCd, Home, BounceT = -9; public int Pol; public float Born; public bool Remix; // Pol：kai-改极性；Remix：改版角色发出的（走新逻辑）
        public float RetT = -1; public bool Sling; // 第九版：回旋（到 0 时沿原路飞回）、奇点弹弓
        public float BounceT2 = -9; // Home：追踪转向速度（弧度/秒）；BounceT：上次反弹的时间（DVD 角落判定）
    }

    /// <summary>dzy 的触手：种在地上，自动抽打附近的敌人。</summary>
    public sealed class Tentacle { public Player Owner; public float X, Y, T, Cd, Lash; public float LashX, LashY; }
    public sealed class Clone { public byte Kind; public int Hits; public float Hp, Cd; // Kind：0 DVD 分身 1 px 假身 2 ccld-改泥人 3 dzy-改小菌怪
        public int Tgt = -1; public Player Owner; public float X, Y, W, H, Vx, Vy, T; public int Face; public bool OnGround; public float Cx => X + W / 2; public float Cy => Y + H / 2; }
    public sealed class Hole { public float X, Y, T, Delay, Dur, Rmax, R; public Player Owner; public bool Done; public bool Active => T >= Delay; }
    /// <summary>从天上掉的东西：冰锥（冰川地图）或流星（事件）。</summary>
    public sealed class Falling { public float X, Y, Vy, Warn; public bool Meteor, Dead; }
    /// <summary>新角色/机关在场上的小物件（地雷、锚点、碟片、白洞、菌毯……）。</summary>
    public enum GKind : byte { Mine = 1, Spike = 2, Anchor = 3, StuckDisc = 4, BigDisc = 5, Patch = 6, WaterWall = 7, Sun = 8, WhiteHole = 9, Vine = 10, Singularity = 11 }
    public sealed class Gadget
    {
        public GKind Kind; public Player Owner; public float X, Y, Vx, Vy, R, T, Arm, Tick, Born; public int Dir = 1; public bool Dead;
        public Dictionary<int, float> HitT = new Dictionary<int, float>();
    }
    public sealed class Item { public float X, Y, W = 26, H = 24, Vy, Life = 16; public int Type; public bool Landed; public float TpCd; }

    public enum FxType : byte { Burst, Text, Ring, Shake, Flash, Sound, Hit, Zoom, Dig, Zap, Raise }
    public enum Sfx : byte { Hit, Heavy, Boom, Jump, Swing, Shoot, Disc, Hole, God, TimeStop, Pickup, KO, Slam, Beep, BeepGo, Clone, Absorb, Wave }

    /// <summary>一次性特效事件。房主产生，联机时带编号下发给朋友，丢包也会重发直到被确认。</summary>
    public struct Fx
    {
        public FxType Type; public float X, Y, A, B, C; public uint Color; public string Text; public bool Big; public int Id;
    }

    public static class Items
    {
        public static readonly string[] Names = { "炸弹", "回血", "疾跑", "狂暴", "巨人", "冰雷", "泡泡", "弹簧", "骰子", "终极球" };
        public static readonly uint[] Colors = { 0xff5464, 0x5dff8f, 0x5ab4ff, 0xff9a3c, 0xc04cff, 0x7fd6ff, 0xa8fff0, 0xffe27a, 0xff6bd6, 0xfff2a8 };
        public static readonly string[] Desc = { "下次攻击扔出炸弹", "回复 35 生命", "7 秒移速 +40%", "8 秒伤害 ×1.5", "8 秒变大，伤害 ×1.3、更难击飞", "下次攻击扔出冰雷，冻住周围敌人 1.5 秒", "挡下一次伤害（10 秒内）", "10 秒跳得更高、可三段跳", "所有人随机交换位置！", "每局第 30 秒在空中出现一次，飘来飘去，抢到后下一次按技能放「终极爆发」" };
        public const int Count = 9, Dice = 8, Ult = 9; // Count：会随机掉落的种类数；终极球（9）每局只出现一次
    }
}
