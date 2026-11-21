// 四人乱斗场 · Unity 版 —— 纯 C# 核心（不依赖 UnityEngine，可单独编译测试）
// 坐标系沿用网页版：x 向右、y 向下，单位像素，场地 1920×1080（第四版放大到 1.5 倍），格子 20px。
using System;
using System.Collections.Generic;

namespace Brawl.Core
{
    public enum CharId : byte { DVD = 0, XP = 1, BW = 2, CCLD = 3, GARY = 4, KAI = 5, DZY = 6, PX = 7, CCLD2 = 8, DVD2 = 9, XP2 = 10, BW2 = 11, GARY2 = 12, KAI2 = 13, DZY2 = 14 }
    public enum SlotMode : byte { Off = 0, Human = 1, Cpu = 2, Net = 3 }
    public enum GameState : byte { Menu, Countdown, Play, RoundEnd, MatchEnd, Paused }
    public enum GameMode : byte { Classic = 0, Crown = 1, Team = 2, Training = 3 }
    public enum MapHazard : byte { None, Ice, Wind, Portal, Train, Mushroom, Space, Forge, Bridge }

    public sealed class CharDef
    {
        public CharId Id; public string Name, Role; public uint Color;
        public float Hp, Speed, Jump, AtkCd, SkCd; public int W, H;
        public string[] StatNames; public int[] Stats; public string Atk, Sk;
    }

    public static class Defs
    {
        public const int T = 20, COLS = 96, ROWS = 54, W = 1920, H = 1080; // 标准地图尺寸；大地图在 MapDef.Cols/Rows 里单独给（Sim.COLS 等是当前地图的实际尺寸）
        public const int MAX_COLS = 240, MAX_ROWS = 108;
        public const float GRAV = 1900f;
        // 与网页版一致的全局倍率（“打得爽一些”那一版）
        public const float HP_MULT = 1.6f, DMG_MULT = 1.45f, KB_MULT = 1.15f, SPD_MULT = 1.2f, JUMP_MULT = 1.1f, ATK_MULT = .78f, CD_MULT = .7f;
        public const float LAVA_AT = 45f, SUDDEN_AT = 120f;
        public const float SLOW_K = .3f; // ccld 超频时其他人的时间流速
        public const int WIN_ROUNDS = 3;
        public const int DVD_ULT = 3; // DVD 攒 3 层就能开十重分身
        public const int MAX_P = 10; // 最多 10 人（联机房主可设 2~10）
        public const float COYOTE = .09f, JUMP_BUFFER = .11f; // 手感包：土狼时间 + 跳跃缓冲

        public static readonly uint[] SlotColors = { 0xff6b6b, 0x5ab4ff, 0x7dffb0, 0xffd84a, 0xff9ad5, 0xb8ff5a, 0xff9a3c, 0x9a7bff, 0x3cf0e0, 0xf0f0f0 };
        public static readonly uint[] TeamColors = { 0xff4d5e, 0x3d9bff }; // 团队模式：红队（P1/P3/P5）、蓝队（P2/P4/P6）
        public static readonly string[] TeamNames = { "红队", "蓝队" };
        public static readonly CharId[] Order = { CharId.DVD, CharId.XP, CharId.BW, CharId.CCLD, CharId.GARY, CharId.KAI, CharId.DZY, CharId.PX,
            CharId.DVD2, CharId.XP2, CharId.BW2, CharId.CCLD2, CharId.GARY2, CharId.KAI2, CharId.DZY2 }; // 第二排是「-改」版，排在原版正下方
        public static bool IsRemix(CharId c) => c >= CharId.CCLD2;
        /// <summary>改版对应的原版（用来复用贴图）。</summary>
        public static CharId BaseOf(CharId c) => c == CharId.CCLD2 ? CharId.CCLD : c == CharId.DVD2 ? CharId.DVD : c == CharId.XP2 ? CharId.XP : c == CharId.BW2 ? CharId.BW : c == CharId.GARY2 ? CharId.GARY : c == CharId.KAI2 ? CharId.KAI : c == CharId.DZY2 ? CharId.DZY : c;

        public static readonly CharDef[] Chars =
        {
            new CharDef{ Id=CharId.DVD, Name="DVD", Color=0xff4fd8, Role="叠层射手 · 十重分身", Hp=158, Speed=310, Jump=720, W=36, H=28, AtkCd=.75f, SkCd=1,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{75,98,92,95},
                Atk="光盘：像 DVD 屏保一样在墙和地面之间反弹 4 次，每弹一次更快、更疼（+2 伤害）；0.35 秒内连弹两次就是「撞角落」，原地爆炸 16 伤害并直接 +2 层。命中让敌人流血并叠 1 层。满 6 层后变成机关枪狂射：攻速拉满但每发只有 3 伤害，命中给自己回血。几秒不命中层数会掉。",
                Sk="十重分身（攒到 3 层就能开）：一下变成 10 个，长得一模一样，只有你自己知道哪个是真身。开大期间真身不能攻击，9 个分身分头去追不同的敌人，碰到就小爆炸（每个伤害不高，但一起炸很疼）。"},
            new CharDef{ Id=CharId.XP, Name="xp", Color=0x4cff8a, Role="抗揍胖子 · 越挨揍越疼", Hp=155, Speed=260, Jump=660, W=46, H=46, AtkCd=.6f, SkCd=5,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{100,35,75,85},
                Atk="肚皮撞：向前冲刺顶出去，全程霸体，13 伤害并击飞。把人顶到墙上会让他眩晕 0.8 秒并追加 6 伤害。",
                Sk="泰山压顶（冷却 5 秒）：起跳时把周围 240 像素内的人吸过来，然后高高跳起，上升和滞空无敌，空中慢慢跟着最近的敌人。落地砸大坑，周围 22 伤害，僵直 0.5 秒、减速 40% 持续 2 秒，还能砸破护盾。被动·铁肚皮：受伤 -20%，免疫击退；每挨 3 下，下一次肚皮撞 +50%。"},
            new CharDef{ Id=CharId.BW, Name="bw", Color=0xc77dff, Role="黑洞 · 越吸越大", Hp=148, Speed=270, Jump=700, W=30, H=34, AtkCd=.55f, SkCd=7,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{88,88,90,98},
                Atk="小黑洞：射得更快、会自己拐弯追人，本身伤害很低，主要靠 DOT：打中会叠坍缩层持续掉血，并带 3 秒侵蚀燃烧（身上有层/燃烧时受到所有伤害 +15%）。被吸住会往黑洞拉、移动减半，能自己走出去。满 6 层坍缩爆炸：解除吸附，并朝场上其他所有人射出高速黑洞弹。中过 DOT 的人死掉时也会坍缩爆炸。按住下 + 攻击：大范围吞噬，把范围内的人猛吸过来一次（伤害低、叠 1 层）。参与击杀体型变大，所有弹幕和范围一起变大。",
                Sk="吞噬盾：1.8 秒内变成黑紫色漩涡，吞掉打向自己的所有子弹（期间不能攻击），结束时变成黑紫色子弹从对方身边打回去（伤害 ×1.2）。死亡：尸体变成越来越大的黑洞，最大覆盖大半个屏幕。"},
            new CharDef{ Id=CharId.CCLD, Name="ccld", Color=0xffc531, Role="剑圣 · 创世之神", Hp=126, Speed=290, Jump=720, W=30, H=38, AtkCd=.4f, SkCd=2.5f,
                StatNames=new[]{"生命","速度","攻击","神化"}, Stats=new[]{85,90,92,100},
                Atk="剑客形态：近身三连斩 10 / 10 / 16，第 3 段上挑把人挑起来。神力槽随时间、打人和挨打充满；单挑时神力 3 倍速（孤高剑心）。",
                Sk="神力没满：瞬步 · 一闪，向前闪斩一刀。创世神降临（神力满时按技能）：超频 5 秒——你更快，其他人和子弹都慢成 30%，但超频期间伤害只有一半；同时开天辟地，在空中造出新的浮岛。永久成神：自由飞行、受伤 -20%、慢慢回血，身边 3 颗星辰组成星环，灼烧靠近的人。攻击变成横贯全图的剑气，每挥一次倍率 +0.06（最高 ×3）。成神后再按技能放「创世 · 星落」：满天星剑砸下（吃倍率），再造 3 块新浮岛，并回一点血。"},
            new CharDef{ Id=CharId.GARY, Name="gary", Color=0xff8a3c, Role="钩锁刺客 · 背刺", Hp=180, Speed=330, Jump=760, W=28, H=32, AtkCd=.32f, SkCd=3.5f,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{72,100,90,92},
                Atk="苦无：快速连射苦无（14 伤害），能削地形。被动·背刺：从背后打中敌人伤害 ×1.8。",
                Sk="钩锁（冷却 3.5 秒）：朝瞄准方向甩出钩爪。钩到地形就把自己高速拉过去（拉动中短暂无敌，结束时小跳并重置二段跳）；钩到敌人就把他拽到身边、打 10 伤害并僵直 0.5 秒，攻击冷却立刻刷新。"},
            new CharDef{ Id=CharId.KAI, Name="kai", Color=0x3cf0ff, Role="雷霆拳师 · 连锁闪电", Hp=142, Speed=315, Jump=740, W=30, H=34, AtkCd=.26f, SkCd=5,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{80,92,86,94},
                Atk="掌心雷：朝前方瞬间放出一道短闪电（约 220 像素，8 伤害），打中就攒 1 格电荷（最多 5 格，6 秒不打人就散掉）。电荷越多普攻和雷遁越疼（每格 +12%，满格 +60%）。连续第 3 下是连锁闪电：11 伤害，并弹到附近最多 2 个敌人（各 8 伤害 + 短暂麻痹）。",
                Sk="雷遁（冷却 5 秒）：朝瞄准方向瞬移一段距离，穿过的敌人被电到（9 伤害 + 麻痹 0.45 秒），瞬移中无敌。电荷满 5 格时消耗全部电荷，落点再劈下一道天雷：周围 28 伤害、炸飞并炸开地形。"},
            new CharDef{ Id=CharId.DZY, Name="dzy", Color=0xe0457b, Role="触手召唤 · 献祭进化", Hp=205, Speed=285, Jump=710, W=32, H=34, AtkCd=3.85f, SkCd=6,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{80,80,70,96},
                Atk="种触手（每 3 秒一次）：在面前地上种一根触手，触手破土时顶飞脚边的人（8 伤害），之后留在原地 16 秒，每 0.7 秒抽打 215 像素内最近的敌人（9 伤害）。最多同时 6 根；站在自己触手旁边受伤 -25%。进化后普攻变成「触手鞭」：0.5 秒一下，向前甩出 3 条长触手，越进化越长越疼；每第 4 鞭还会顺手种下一根触手。",
                Sk="献祭进化（冷却 6 秒，场上要有触手）：把场上所有触手全部献祭——每根触手原地炸开（11 伤害），然后变成血肉飞回 dzy。每献祭 1 根：最大生命 +10 并回 15 血、伤害 +7%、体型变大。第一次献祭直接进化成触手怪，可以一直叠（最多 12 级）。"},
            new CharDef{ Id=CharId.PX, Name="px", Color=0x8fd14f, Role="霰弹猎手 · 钩子与假身", Hp=185, Speed=310, Jump=720, W=30, H=36, AtkCd=.72f, SkCd=4.5f,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{84,88,90,94},
                Atk="霰弹枪（0.72 秒一发）：中距离散射 6 颗弹丸，离得越近伤害越高；离上次开枪越久，这一枪越疼（最多攒 2.5 秒 +60%）。打中身上有印记的敌人会引爆印记：额外 10 伤害并给自己回 8 血。按住下 + 攻击：脚下埋地雷（最多 3 颗），敌人踩到或 4 秒后爆炸。",
                Sk="钩子（冷却 4.5 秒）：朝前方甩出钩子，穿墙勾住面前最近的敌人，飞快拉到自己面前（6 伤害 + 印记 + 短暂僵直）。每勾中 1 人叠 1 层，满 3 层后下一钩勾住面前所有敌人。按住下 + 技能：原地留下假身（和你一模一样），敌人打它 3 下就爆炸；你自己普攻或放钩子时它也立刻爆炸。爆炸 12 伤害并给炸到的所有人上印记。"},
            new CharDef{ Id=CharId.CCLD2, Name="ccld-改", Color=0xf4f0d8, Role="居合剑客 · 七日创世神", Hp=136, Speed=300, Jump=730, W=30, H=38, AtkCd=.4f, SkCd=3,
                StatNames=new[]{"生命","速度","攻击","神化"}, Stats=new[]{84,92,90,98},
                Atk="三段斩：1、2 段 12 伤害，第 3 段向前突进 17 伤害并放出一道短剑气。剑尖：刀的最远端打中伤害 ×1.4。成神后每一刀都带一道 300 像素剑气（10 伤害）。",
                Sk="见切（冷却 3 秒）：架势 0.3 秒，期间被打中就免伤、全场变慢、瞬移到对方身后居合斩 22 伤害，冷却立刻刷新；没格到会硬直一下。神格槽满了再按技能「神降」：光柱劈下，全场震荡（18 伤害 + 眩晕），2.5 秒无敌；之后身形放大、通体金光，可以三段跳、按住跳滑翔，受伤 -20%。成神后技能变成「七日」，每按一次放下一天的神通（冷却 3.8 秒），全都是全图级：光（全场致盲 + 每人头顶落光矛）、分开诸水（两道巨浪扫过整张地图）、大地（每个敌人脚下同时拔起石柱，再被藤蔓缠住）、日月（追人的巨大太阳 + 流星）、飞鸟（12 只光鸟先亮预警圈再直线俯冲）、造人（3 个泥人）、安息（3 秒无敌回血，全场敌人挨一记审判光矛、技能冷却 +3 秒）。"},
            new CharDef{ Id=CharId.DVD2, Name="DVD-改", Color=0x9a7bff, Role="屏保弹射手 · 回收碟片", Hp=145, Speed=305, Jump=720, W=36, H=28, AtkCd=.5f, SkCd=9,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{72,90,92,92},
                Atk="弹射碟（3 片弹药）：直线射出，12 伤害，撞墙反弹最多 4 次，每弹一次伤害 +25%、速度 +10%，能穿过敌人（每人只打一次）。打进地形的角落触发「完美角落」：原地爆炸 18 伤害并返还 1 片弹药。弹完的碟片插在地上，走过去就能捡回来，6 秒后也会自己飞回来。没弹药时普攻变成近身挥砍。",
                Sk="巨型屏保（冷却 9 秒）：放出一个巨大的发光碟标，45° 斜着飞、撞墙反弹，在全场乱撞 6 秒，碰到敌人 14 伤害并把人带走一段，每撞一次角落都爆炸。期间弹药上限 +2。"},
            new CharDef{ Id=CharId.XP2, Name="xp-改", Color=0xffb3d1, Role="吞吞滚球 · 吞人吐人", Hp=180, Speed=250, Jump=640, W=44, H=44, AtkCd=.7f, SkCd=4.5f,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{100,40,74,85},
                Atk="吞：吸前方的敌人，吸到就整个吞进肚子（最多含 1.2 秒，对方乱按能早点出来），再按普攻把他当炮弹吐出去：撞到墙或别人时双方都 16 伤害；被吐的人自己也吃 10 伤害。也能吞子弹，再吐出来变成自己的子弹。什么都没吸到就拍一下肚皮。",
                Sk="滚滚（冷却 4.5 秒）：缩成球滚 2.5 秒，速度很快、撞墙反弹，撞到人造成和速度相关的伤害并击飞，滚动中受伤 -40%。被动·弹力肚：从上面踩到敌人头会弹起来并造成 9 伤害；掉出地图只扣一半血。"},
            new CharDef{ Id=CharId.BW2, Name="bw-改", Color=0xe8f4ff, Role="白洞 · 斥力击飞", Hp=110, Speed=275, Jump=700, W=30, H=34, AtkCd=.6f, SkCd=8,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{80,85,75,96},
                Atk="斥力波：慢速的白色弧形波（不追踪，看得见、躲得开），6 伤害、超强击退。被推开的人 0.6 秒内撞到地形会受到撞墙伤害（最高 22）并眩晕。每把人撞墙或推出地图攒 1 格能量，满 3 格下一次普攻变成全方向的环形波。按住下 + 攻击：推开身边所有人和子弹（子弹反弹回去变成你的）。",
                Sk="事件视界（冷却 8 秒）：在前方放下一个白洞，持续 3 秒：进来的子弹全部掉头、敌人被一直往外推，你自己在里面受伤 -30%。结束时向外爆一圈 12 伤害。"},
            new CharDef{ Id=CharId.GARY2, Name="gary-改", Color=0x3cbf9a, Role="绊索机关师 · 控场", Hp=175, Speed=300, Jump=740, W=28, H=32, AtkCd=.45f, SkCd=6,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{78,88,78,94},
                Atk="钉镖：15 伤害，打到地形就钉住成为锚点（最多 4 个）。两个相距不远、中间没有遮挡的锚点之间自动拉出绊索，敌人碰到 7 伤害并绊倒。按住下 + 攻击：脚下埋隐形地钉（最多 2 枚），敌人踩到 10 伤害并弹到空中。",
                Sk="收网（冷却 6 秒）：所有绊索同时收紧，把绊索附近的敌人拉向中点，12 伤害 + 僵直，撞在一起的人越多越疼，然后锚点清空。场上没有绊索时变成甩绳把自己拉过去。"},
            new CharDef{ Id=CharId.KAI2, Name="kai-改", Color=0xff6a8a, Role="极性磁暴 · 连招发起", Hp=162, Speed=305, Jump=730, W=30, H=34, AtkCd=.34f, SkCd=5.5f,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{80,90,82,96},
                Atk="极性弹：交替射出红（＋）蓝（－）电弹，13 伤害，打中给敌人挂上这个极性 5 秒。和你自身同极性的敌人受到你的伤害 +15%；异极性的敌人靠近你会被吸过来。",
                Sk="磁暴（冷却 5.5 秒）：引爆全场极性标记——异极性的敌人互相猛吸撞在一起（各 18 伤害 + 麻痹），同极性的敌人互相弹开（各 8 伤害 + 强击退）；只有一个人带标记就把他拉过来或推开。之后你的极性切换。"},
            new CharDef{ Id=CharId.DZY2, Name="dzy-改", Color=0x6bd94a, Role="菌毯领地 · 潜行", Hp=175, Speed=280, Jump=700, W=32, H=34, AtkCd=.48f, SkCd=6,
                StatNames=new[]{"生命","速度","攻击","技能"}, Stats=new[]{85,82,75,92},
                Atk="孢子炮：抛出一团孢子，命中 10 伤害，落地铺开一片菌毯（14 秒）；自己走过的地面也会留下菌毯。站在自己的菌毯上：移速 +30%、每秒回 3 血、受伤 -15%；敌人站上去会变慢并持续掉血。敌人死在菌毯上会冒出一只追人自爆的小菌怪。",
                Sk="潜菌（冷却 6 秒）：钻进脚下的菌毯 2 秒，变成看不见的波纹高速移动，期间无敌；再按技能或时间到就钻出来咬一口：周围 18 伤害并把人拉到身边。"},
        };
        public static CharDef Char(CharId id) => Chars[(int)id];

        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        public static float Hypot(float x, float y) => (float)Math.Sqrt(x * x + y * y);
        public static int Sign(float v) => v > 0 ? 1 : (v < 0 ? -1 : 0);
    }

    public sealed class MapDef
    {
        public string Name; public int[][] Spawn; public Action<byte[]> Build; public MapHazard Hazard; public int[][] Portals; // 传送门：ax,ay,bx,by,颜色
        public uint Ice = 0x9fe4ff;
        public uint SkyTop, SkyBottom, Dirt, DirtDark, Grass, GrassLight, Stone, StoneLight, Hill; // 每张图的调色板
        public int Cols = Defs.COLS, Rows = Defs.ROWS; public bool Native; // Native：按实际格子设计（大地图），不再 ×1.5
        public bool Big, TeamOnly; public float LowGravX0 = -1, LowGravX1 = -1; // 低重力区（像素 x 范围）
        public int W => Cols * Defs.T; public int H => Rows * Defs.T;
    }

    public static class Maps
    {
        // 地图按 64×36 的旧网格设计，建图时整体放大到 96×54（×1.5）
        public const float SCALE = 1.5f;
        static int S0(int v) => (int)Math.Floor(v * SCALE);
        static int S1(int v) => (int)Math.Floor((v + 1) * SCALE) - 1;
        static int BC = Defs.COLS, BR = Defs.ROWS; // 正在建的地图的格子尺寸
        static void Fill(byte[] b, int c0, int c1, int r0, int r1, byte v)
        {
            c0 = S0(c0); c1 = Math.Min(BC - 1, S1(c1)); r0 = S0(r0); r1 = Math.Min(BR - 1, S1(r1));
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) b[r * BC + c] = v;
        }
        /// <summary>大地图用：直接按实际格子填（不缩放）。</summary>
        public static void F(byte[] b, int c0, int c1, int r0, int r1, byte v)
        {
            c0 = Math.Max(0, c0); r0 = Math.Max(0, r0); c1 = Math.Min(BC - 1, c1); r1 = Math.Min(BR - 1, r1);
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) b[r * BC + c] = v;
        }
        public static void Build(byte[] b, int idx)
        {
            var m = All[idx]; BC = m.Cols; BR = m.Rows; Array.Clear(b, 0, b.Length); m.Build(b); Detail(b, idx);
            if (m.Native) foreach (var sp in m.Spawn)
                {   // 出生点贴到正下方的地面上（细节步骤可能削掉了平台边角）
                    int c = sp[0] / Defs.T;
                    for (int r = Math.Max(2, (sp[1] - 40) / Defs.T); r < Math.Min(BR, sp[1] / Defs.T + 16); r++)
                        if (Sol(b, c, r) && !Sol(b, c, r - 1) && !Sol(b, c, r - 2)) { sp[1] = r * Defs.T - 2; break; }
                }
            BC = Defs.COLS; BR = Defs.ROWS;
        }
        static Maps()
        {
            foreach (var m in All)
            {
                if (m.Native) continue;
                foreach (var sp in m.Spawn) { sp[0] = (int)(sp[0] * SCALE); sp[1] = (int)(sp[1] * SCALE); }
                if (m.Portals != null) foreach (var pt in m.Portals) for (int k = 0; k < 4; k++) pt[k] = (int)(pt[k] * SCALE);
            }
        }
        static bool Sol(byte[] b, int c, int r) => c >= 0 && c < BC && r >= 0 && r < BR && b[r * BC + c] > 0;
        static bool Empty(byte[] b, int c0, int c1, int r0, int r1)
        {
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) { if (c < 0 || c >= BC || r < 0 || r >= BR) return false; if (b[r * BC + c] > 0) return false; }
            return true;
        }
        /// <summary>地形细节：确定性（只取决于地图编号），房主和朋友端建出来一模一样。</summary>
        public static void Detail(byte[] b, int idx)
        {
            var rng = new Random(idx * 7919 + 13); var m = All[idx]; int C = m.Cols, R = m.Rows; BC = C; BR = R;
            // 0) 平台加宽：每段可站立的平台两头各延长 5 格（离地图边缘至少留 2 格），不容易一脚踩空掉下去
            for (int pass = 0; pass < (m.Native ? 0 : 5); pass++)
            {
                var add = new List<int>();
                for (int r = 1; r < R; r++) for (int c = 1; c < C - 1; c++)
                    {
                        byte v = b[r * C + c]; if (v == 0) continue;
                        if (c - 1 >= 2 && b[r * C + c - 1] == 0 && Empty(b, c - 1, c - 1, r - 3, r - 1)) add.Add(r * C + c - 1);
                        if (c + 1 <= C - 3 && b[r * C + c + 1] == 0 && Empty(b, c + 1, c + 1, r - 3, r - 1)) add.Add(r * C + c + 1);
                    }
                foreach (var i in add) b[i] = b[i] == 0 ? (byte)(m.Hazard == MapHazard.Ice ? 3 : 1) : b[i];
            }
            // 1) 实心土块深处嵌一些打不坏的石头
            for (int r = 1; r < R - 1; r++) for (int c = 1; c < C - 1; c++)
                    if (b[r * C + c] == 1 && Sol(b, c - 1, r) && Sol(b, c + 1, r) && Sol(b, c, r - 1) && Sol(b, c, r + 1) && Sol(b, c, r - 2) && rng.NextDouble() < .05) b[r * C + c] = 2;
            // 2) 厚平台的边角削成台阶
            for (int r = 1; r < R - 1; r++) for (int c = 0; c < C; c++)
                {
                    if (b[r * C + c] != 1 || Sol(b, c, r - 1) || !Sol(b, c, r + 1)) continue;
                    bool endL = !Sol(b, c - 1, r), endR = !Sol(b, c + 1, r);
                    if ((endL || endR) && rng.NextDouble() < .6) b[r * C + c] = 0;
                }
            // 3) 平台底下挂小钟乳石（不堵路：下面要留出 3 格空）
            for (int r = 1; r < R - 4; r++) for (int c = 1; c < C - 1; c++)
                {
                    if (b[r * C + c] != 1 || Sol(b, c, r + 1) || !Sol(b, c, r - 1)) continue;
                    if (rng.NextDouble() < .16 && Empty(b, c, c, r + 1, r + 4)) { b[(r + 1) * C + c] = 1; if (rng.NextDouble() < .4 && Empty(b, c, c, r + 2, r + 5)) b[(r + 2) * C + c] = 1; }
                }
            // 4) 大片空地里加几块小浮台，地图大了也好落脚
            int placed = 0;
            for (int k = 0; k < 300 && placed < (m.Big ? 14 : 8); k++)
            {
                int w = 3 + rng.Next(3), c = 3 + rng.Next(C - w - 6), r = 8 + rng.Next(R - 20);
                if (!Empty(b, c - 3, c + w + 2, r - 5, r + 4)) continue;
                bool nearSpawn = false; foreach (var sp in m.Spawn) if (Math.Abs(sp[0] - (c + w / 2f) * Defs.T) < 120 && Math.Abs(sp[1] - r * Defs.T) < 140) nearSpawn = true;
                if (m.Portals != null) foreach (var pt in m.Portals) if (Math.Abs(pt[0] - c * Defs.T) < 100 && Math.Abs(pt[1] - r * Defs.T) < 120 || Math.Abs(pt[2] - c * Defs.T) < 100 && Math.Abs(pt[3] - r * Defs.T) < 120) nearSpawn = true;
                if (nearSpawn) continue;
                byte v = m.Hazard == MapHazard.Ice ? (byte)3 : (byte)1;
                for (int q = c; q < c + w; q++) b[r * C + q] = v; placed++;
            }
        }

        public static readonly MapDef[] All = Concat(new[]
        {
            new MapDef{ Name="浮空岛", Spawn=new[]{ new[]{200,540}, new[]{1080,540}, new[]{330,400}, new[]{950,400} },
                SkyTop=0x0b1030, SkyBottom=0x2a1a55, Dirt=0x4a3a6e, DirtDark=0x34284f, Grass=0x8f7cff, GrassLight=0xc9bfff, Stone=0x343a66, StoneLight=0x4a5290, Hill=0x161a3e,
                Build=b=>{ Fill(b,8,55,27,31,1);Fill(b,12,51,32,32,1);Fill(b,18,45,33,33,1);Fill(b,24,39,34,34,1);Fill(b,27,36,32,33,2);
                    Fill(b,11,21,20,21,1);Fill(b,42,52,20,21,1);Fill(b,26,37,14,15,1);Fill(b,2,6,22,22,1);Fill(b,57,61,22,22,1);} },
            new MapDef{ Name="双塔对峙", Spawn=new[]{ new[]{200,280}, new[]{1080,280}, new[]{520,620}, new[]{760,620} },
                SkyTop=0x1a0f24, SkyBottom=0x5a2a3a, Dirt=0x6a4038, DirtDark=0x4a2c28, Grass=0xffa05a, GrassLight=0xffd3a0, Stone=0x3c2f40, StoneLight=0x5a4660, Hill=0x2a1626,
                Build=b=>{ Fill(b,6,13,14,31,1);Fill(b,50,57,14,31,1);Fill(b,8,11,28,31,2);Fill(b,52,55,28,31,2);
                    Fill(b,14,49,22,22,1);Fill(b,18,45,31,32,1);Fill(b,29,34,32,32,2);Fill(b,24,28,15,15,1);Fill(b,35,39,15,15,1);Fill(b,30,33,10,10,1);} },
            new MapDef{ Name="金字塔", Spawn=new[]{ new[]{200,600}, new[]{1080,600}, new[]{330,520}, new[]{950,520} },
                SkyTop=0x08182a, SkyBottom=0x1f4a5a, Dirt=0x8a6a3a, DirtDark=0x62492a, Grass=0xffd86a, GrassLight=0xfff0b0, Stone=0x3a4a58, StoneLight=0x56707e, Hill=0x0e2a36,
                Build=b=>{ Fill(b,8,55,30,33,1);Fill(b,14,49,26,29,1);Fill(b,20,43,22,25,1);Fill(b,26,37,18,21,1);Fill(b,30,33,30,33,2);
                    Fill(b,2,8,16,16,1);Fill(b,55,61,16,16,1);Fill(b,29,34,10,10,1);} },
            new MapDef{ Name="冰川裂谷", Hazard=MapHazard.Ice, Spawn=new[]{ new[]{160,520}, new[]{1120,520}, new[]{440,300}, new[]{840,300} },
                SkyTop=0x061428, SkyBottom=0x1d4a6a, Dirt=0x5a7fa8, DirtDark=0x3c5a80, Grass=0xd8f4ff, GrassLight=0xffffff, Stone=0x2c3e5a, StoneLight=0x45618a, Hill=0x0f2840,
                Build=b=>{ Fill(b,2,14,27,35,2);Fill(b,49,61,27,35,2);Fill(b,2,14,26,26,3);Fill(b,49,61,26,26,3);Fill(b,15,48,25,25,3);
                    Fill(b,18,26,16,16,3);Fill(b,37,45,16,16,3);Fill(b,29,34,9,9,2);Fill(b,0,63,0,0,2);} },
            new MapDef{ Name="风暴之巅", Hazard=MapHazard.Wind, Spawn=new[]{ new[]{260,380}, new[]{1020,380}, new[]{640,150}, new[]{640,470} },
                SkyTop=0x101626, SkyBottom=0x3a4a6a, Dirt=0x5a5a70, DirtDark=0x3e3e52, Grass=0x9ae0b0, GrassLight=0xd0ffe0, Stone=0x30344a, StoneLight=0x4a5070, Hill=0x1a2034,
                Build=b=>{ Fill(b,10,16,20,35,1);Fill(b,29,34,24,35,1);Fill(b,47,53,20,35,1);Fill(b,11,15,33,35,2);Fill(b,30,33,33,35,2);Fill(b,48,52,33,35,2);
                    Fill(b,20,26,14,14,1);Fill(b,37,43,14,14,1);Fill(b,28,35,8,8,1);Fill(b,0,5,28,28,1);Fill(b,58,63,28,28,1);} },
            new MapDef{ Name="传送门工厂", Hazard=MapHazard.Portal, Spawn=new[]{ new[]{180,560}, new[]{1100,560}, new[]{400,380}, new[]{880,380} },
                Portals=new[]{ new[]{60,556,1220,176,0xff9a3c}, new[]{1220,556,60,176,0x5ab4ff} },
                SkyTop=0x140c1e, SkyBottom=0x3a2440, Dirt=0x5a4a5a, DirtDark=0x3e3040, Grass=0xffb04a, GrassLight=0xffe0a0, Stone=0x2e2a3a, StoneLight=0x4a4458, Hill=0x221a2c,
                Build=b=>{ Fill(b,0,63,31,35,2);Fill(b,4,59,30,30,1);Fill(b,14,49,21,21,1);Fill(b,0,6,11,11,1);Fill(b,57,63,11,11,1);Fill(b,26,37,12,12,1);Fill(b,30,33,13,20,1);} },
        }, BigMaps.Make());
        static MapDef[] Concat(MapDef[] a, MapDef[] b) { var r = new MapDef[a.Length + b.Length]; a.CopyTo(r, 0); b.CopyTo(r, a.Length); return r; }
        public static int StandardCount => 6;
    }
}
