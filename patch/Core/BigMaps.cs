// 第五版新增的大地图：直接按实际格子设计（144×81 或 180×81），每张带一个机关。
// 格子值：1 土（可破坏） 2 石/金属（打不坏） 3 冰 4 弹跳蘑菇 5 向右传送带 6 向左传送带
using static Brawl.Core.Maps;

namespace Brawl.Core
{
    public static class BigMaps
    {
        // 出生点顺序：偶数号（红队）在左半边，奇数号（蓝队）在右半边
        public static MapDef[] Make() => new[]
        {
            new MapDef{ Name="天空列车", Native=true, Big=true, Cols=180, Rows=81, Hazard=MapHazard.Train,
                Spawn=new[]{ new[]{300,998}, new[]{3300,998}, new[]{860,998}, new[]{2740,998}, new[]{1440,998}, new[]{2160,998}, new[]{560,1238}, new[]{3040,1238} },
                SkyTop=0x0d1a3a, SkyBottom=0x6a8fd0, Dirt=0x8a3a3a, DirtDark=0x5a2424, Grass=0xffc94a, GrassLight=0xfff0a8, Stone=0x3a4258, StoneLight=0x5a6684, Hill=0x2a3a64,
                Build=b=>{
                    for (int k = 0; k < 6; k++)
                    {
                        int c0 = 4 + k * 29, c1 = c0 + 25;
                        F(b, c0, c1, 50, 51, 1);              // 车顶（可破坏）
                        F(b, c0, c0, 52, 57, 2); F(b, c1, c1, 52, 57, 2); // 车厢两头的墙，下面留门
                        F(b, c0, c1, 62, 63, 2);              // 车厢地板
                        F(b, c0 + 3, c0 + 6, 64, 65, 2); F(b, c1 - 6, c1 - 3, 64, 65, 2); // 车轮
                        F(b, c0 + 10, c0 + 15, 57, 57, 1);   // 车厢里的行李架
                    }
                    F(b, 30, 40, 34, 34, 1); F(b, 70, 80, 30, 30, 1); F(b, 100, 110, 30, 30, 1); F(b, 140, 150, 34, 34, 1); // 信号架
                } },
        };
        // 先只上一张大地图（evan：地图先做一个就行）。下面几张做好了，以后再放进地图池。
        public static MapDef[] Later() => new[]
        {
            new MapDef{ Name="蘑菇森林", Native=true, Big=true, Cols=144, Rows=81, Hazard=MapHazard.Mushroom,
                Spawn=new[]{ new[]{300,1078}, new[]{2580,1078}, new[]{920,918}, new[]{1960,918}, new[]{400,1438}, new[]{2420,1438}, new[]{1440,598}, new[]{1380,1158} },
                SkyTop=0x0a1424, SkyBottom=0x24405a, Dirt=0x5a4030, DirtDark=0x3c2a20, Grass=0x6bd96a, GrassLight=0xc8ffb0, Stone=0x6a5a4a, StoneLight=0x8a7a66, Hill=0x14283a,
                Build=b=>{
                    F(b, 0, 29, 72, 80, 1); F(b, 38, 105, 72, 80, 1); F(b, 114, 143, 72, 80, 1); // 地面（两个深坑）
                    F(b, 6, 24, 75, 77, 0); F(b, 50, 93, 75, 77, 0); F(b, 120, 138, 75, 77, 0); // 树根洞穴
                    F(b, 0, 143, 78, 80, 2);
                    F(b, 30, 37, 78, 80, 0); F(b, 106, 113, 78, 80, 0);
                    int[][] caps = { new[]{8,21,54}, new[]{40,55,46}, new[]{88,103,46}, new[]{122,135,54}, new[]{62,81,30}, new[]{20,31,34}, new[]{112,123,34}, new[]{66,77,58} };
                    foreach (var c in caps) { F(b, c[0], c[1], c[2], c[2] + 1, 1); int m = (c[0] + c[1]) / 2; F(b, m, m + 1, c[2] + 2, c[2] + 4, 2); } // 短菌柄（不挡地面上的路）
                    F(b, 44, 48, 69, 69, 4); F(b, 95, 99, 69, 69, 4); F(b, 78, 81, 66, 66, 4); F(b, 2, 5, 62, 62, 4); F(b, 138, 141, 62, 62, 4); // 弹跳蘑菇
                    F(b, 46, 46, 70, 71, 2); F(b, 97, 97, 70, 71, 2); F(b, 79, 80, 67, 71, 2);
                } },
            new MapDef{ Name="星港空间站", Native=true, Big=true, Cols=144, Rows=81, Hazard=MapHazard.Space, LowGravX0=1440, LowGravX1=2880,
                Spawn=new[]{ new[]{500,1318}, new[]{2380,1318}, new[]{600,998}, new[]{2280,998}, new[]{700,718}, new[]{2180,718}, new[]{1240,1318}, new[]{1640,1318} },
                SkyTop=0x02030a, SkyBottom=0x101838, Dirt=0x5a6478, DirtDark=0x3a4256, Grass=0x7ff3ff, GrassLight=0xd8ffff, Stone=0x2a3040, StoneLight=0x46506a, Hill=0x0a0e20,
                Build=b=>{
                    F(b, 10, 133, 66, 67, 2); F(b, 10, 133, 14, 15, 2);          // 地板、天花板
                    F(b, 10, 11, 16, 51, 2); F(b, 132, 133, 16, 51, 2);          // 两侧舱壁，下面是气闸门（开着的口子）
                    F(b, 58, 85, 66, 66, 1);                                     // 中间是可打破的玻璃地板
                    F(b, 18, 40, 50, 50, 1); F(b, 103, 125, 50, 50, 1); F(b, 56, 87, 52, 52, 1);
                    F(b, 28, 48, 36, 36, 1); F(b, 95, 115, 36, 36, 1); F(b, 62, 81, 26, 26, 1);
                    F(b, 12, 20, 60, 60, 2); F(b, 123, 131, 60, 60, 2);          // 门口的扶手台
                } },
            new MapDef{ Name="熔岩锻炉", Native=true, Big=true, Cols=144, Rows=81, Hazard=MapHazard.Forge,
                Spawn=new[]{ new[]{300,1398}, new[]{2580,1398}, new[]{700,1078}, new[]{2180,1078}, new[]{1440,838}, new[]{1440,1398}, new[]{400,758}, new[]{2480,758} },
                SkyTop=0x1a0a08, SkyBottom=0x5a2010, Dirt=0x5a4038, DirtDark=0x3a2824, Grass=0xff8a3c, GrassLight=0xffd08a, Stone=0x34303a, StoneLight=0x56505e, Hill=0x2a1410,
                Build=b=>{
                    F(b, 4, 139, 70, 73, 2);
                    F(b, 8, 46, 70, 70, 5); F(b, 97, 135, 70, 70, 6); F(b, 56, 87, 70, 70, 1); // 传送带往中间送（中间是可以打穿的熔渣地板）
                    F(b, 22, 48, 54, 54, 1); F(b, 95, 121, 54, 54, 1); F(b, 60, 83, 42, 42, 1);
                    F(b, 10, 30, 38, 38, 5); F(b, 113, 133, 38, 38, 6);
                    F(b, 36, 44, 8, 11, 2); F(b, 100, 108, 8, 11, 2);            // 液压锤的锤座
                    F(b, 68, 75, 28, 28, 1);
                } },
            new MapDef{ Name="双城巨桥", Native=true, Big=true, TeamOnly=true, Cols=180, Rows=81, Hazard=MapHazard.Bridge,
                Spawn=new[]{ new[]{300,958}, new[]{3300,958}, new[]{520,698}, new[]{3080,698}, new[]{180,558}, new[]{3420,558}, new[]{620,958}, new[]{2980,958} },
                SkyTop=0x101a30, SkyBottom=0x6a5a80, Dirt=0x6a5a48, DirtDark=0x4a3e32, Grass=0x9ad06a, GrassLight=0xe0ffb0, Stone=0x4a4a5a, StoneLight=0x6a6a80, Hill=0x2a2440,
                Build=b=>{
                    F(b, 0, 36, 48, 80, 2); F(b, 143, 179, 48, 80, 2);           // 两座城堡
                    F(b, 4, 32, 48, 49, 1); F(b, 147, 175, 48, 49, 1);
                    F(b, 22, 26, 35, 47, 2); F(b, 153, 157, 35, 47, 2);         // 城墙塔楼
                    F(b, 20, 34, 35, 35, 1); F(b, 145, 159, 35, 35, 1);
                    F(b, 4, 14, 28, 28, 1); F(b, 165, 175, 28, 28, 1);
                    F(b, 37, 142, 48, 49, 1);                                     // 可打断的长桥
                    F(b, 56, 62, 60, 60, 1); F(b, 80, 86, 62, 62, 1); F(b, 93, 99, 62, 62, 1); F(b, 117, 123, 60, 60, 1); // 桥下绳索平台
                    F(b, 70, 78, 36, 36, 1); F(b, 101, 109, 36, 36, 1); F(b, 86, 93, 26, 26, 1);
                } },
        };
    }
}
