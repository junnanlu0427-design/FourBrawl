// 大地图实验室：ASCII 预览 + 出生点检查 + 6/10 人电脑对战（无界面）。
// mcs -langversion:experimental -out:maplab.exe Core/*.cs Tools/MapLab/MapLab.cs && mono maplab.exe [局数倍数]
using System; using System.Collections.Generic; using System.Linq; using System.Text; using System.Threading.Tasks; using Brawl.Core;
static class MapLab {
  static readonly object L = new object();
  static char Ch(byte v)=> v==0?' ':v==1?'#':v==2?'@':v==3?'~':v==4?'^':v==5?'>':v==6?'<':'?';
  public static string Ascii(int i){
    var m=Maps.All[i]; var b=new byte[m.Cols*m.Rows]; lock(L) Maps.Build(b,i);
    var g=new char[m.Rows][]; for(int r=0;r<m.Rows;r++){g[r]=new char[m.Cols]; for(int c=0;c<m.Cols;c++) g[r][c]=Ch(b[r*m.Cols+c]);}
    if(m.Portals!=null) foreach(var pt in m.Portals){ Put(g,pt[1]/20,pt[0]/20,'O'); Put(g,pt[3]/20,pt[2]/20,'O'); }
    for(int k=0;k<m.Spawn.Length;k++) Put(g,m.Spawn[k][1]/20-1,m.Spawn[k][0]/20,(char)('0'+k%10));
    int lava=(int)(m.H*.46f/20);
    var sb=new StringBuilder(); sb.AppendLine($"【{m.Name}】 {m.Cols}×{m.Rows} 格（{m.W}×{m.H} 像素） 机关={m.Hazard}{(m.TeamOnly?" 团队专用":"")}  # 土(可破坏) @ 石(打不坏) ~ 冰 ^ 弹跳蘑菇 > < 传送带 O 传送门 0-9 出生点  ┄ 岩浆最终高度");
    var head=new StringBuilder("    "); for(int c=0;c<m.Cols;c++) head.Append(c%10==0?(char)('0'+(c/10)%10):' '); sb.AppendLine(head.ToString());
    for(int r=0;r<m.Rows;r++){ var line=new string(g[r]); if(r==lava) line=line.Replace(' ','┄'); sb.AppendLine(r.ToString().PadLeft(3)+"|"+line+"|"); }
    return sb.ToString();
  }
  static void Put(char[][] g,int r,int c,char ch){ if(r>=0&&r<g.Length&&c>=0&&c<g[0].Length) g[r][c]=ch; }

  // 出生点：大小两种体型都不嵌墙、脚下 2 像素内是地面；偶数在左、奇数在右
  public static List<string> CheckSpawns(int i){
    var m=Maps.All[i]; var b=new byte[m.Cols*m.Rows]; lock(L) Maps.Build(b,i); var bad=new List<string>();
    Func<int,int,bool> sol=(c,r)=> c<0||c>=m.Cols||r<0||r>=m.Rows ? false : b[r*m.Cols+c]>0;
    if(m.Spawn.Length<10) bad.Add($"只有 {m.Spawn.Length} 个出生点");
    for(int k=0;k<m.Spawn.Length;k++){
      int x=m.Spawn[k][0], y=m.Spawn[k][1];
      if((k%2==0) != (x<m.W/2)) bad.Add($"出生点 {k} 在错的半边 x={x}");
      foreach(var wh in new[]{new[]{46,46},new[]{30,38},new[]{36,28}}){
        float x0=x-wh[0]/2f, y0=y-wh[1];
        for(int r=(int)Math.Floor(y0/20);r<=(int)Math.Floor((y-.01f)/20);r++) for(int c=(int)Math.Floor(x0/20);c<=(int)Math.Floor((x0+wh[0]-.01f)/20);c++) if(sol(c,r)){ bad.Add($"出生点 {k} ({x},{y}) 体型 {wh[0]}×{wh[1]} 嵌在墙里"); goto next; }
        next:;
      }
      int fr=(y+2)/20; bool grd=sol((x-10)/20,fr)||sol(x/20,fr)||sol((x+10)/20,fr);
      if(!grd) bad.Add($"出生点 {k} ({x},{y}) 脚下悬空");
      byte under=b[Math.Min(m.Rows-1,fr)*m.Cols+x/20]; if(under==4) bad.Add($"出生点 {k} 站在弹跳蘑菇上");
      for(int j=0;j<k;j++) if(Math.Abs(m.Spawn[j][0]-x)<60&&Math.Abs(m.Spawn[j][1]-y)<60) bad.Add($"出生点 {k} 和 {j} 太近");
    }
    return bad;
  }

  public struct R { public bool Ended; public float T; public int Stuck; public int Win; public string StuckInfo; public int FallKo; }
  public static R Round(int map,int n,int seed,GameMode mode){
    Sim s; lock(L) s=new Sim(seed); s.Mode=mode; s.MapPick=map; var rng=new Random(seed);
    var ord=Defs.Order.OrderBy(_=>rng.Next()).ToArray();
    for(int i=0;i<Defs.MAX_P;i++){ if(i<n){ s.Slots[i].Mode=SlotMode.Cpu; s.Slots[i].Ch=ord[i%ord.Length]; } else s.Slots[i].Mode=SlotMode.Off; }
    lock(L) s.StartMatch();
    var res=new R{Win=-1}; int steps=0; const int CAP=60*240;
    // 卡住 = 连续 30 秒（每 5 秒看一次，7 次）一直待在 3 格见方的范围里，而且这期间没打到人也没挨打
    var hist=new Dictionary<int,List<float[]>>(); var stuckFlag=new HashSet<int>();
    while(steps<CAP){
      s.Step(); steps++; s.FxOut.Clear();
      if(s.State==GameState.RoundEnd||s.State==GameState.MatchEnd){ res.Ended=true; res.Win=s.WinnerSlot; break; }
      if(Trace && s.State==GameState.Play && steps%(60*10)==0){ Console.WriteLine($"t={s.RoundT:F0} lava={(int)(s.LavaY/20)} "+string.Join(" ",s.Players.Where(p=>p.Alive).Select(p=>$"{p.C.Name}({(int)p.Cx/20},{(int)p.Cy/20}){(int)p.Hp}"))); }
      if(s.State==GameState.Play && steps%(60*5)==0){
        foreach(var p in s.Players){ if(!p.Alive) continue;
          if(!hist.TryGetValue(p.Slot,out var h)) hist[p.Slot]=h=new List<float[]>();
          h.Add(new[]{p.X,p.Y,p.Hp,p.DamageDealt}); if(h.Count>7) h.RemoveAt(0);
          if(h.Count==7 && !stuckFlag.Contains(p.Slot)){
            float x0=h.Min(q=>q[0]),x1=h.Max(q=>q[0]),y0=h.Min(q=>q[1]),y1=h.Max(q=>q[1]);
            if(x1-x0<60 && y1-y0<60 && Math.Abs(h[0][2]-h[6][2])<1 && Math.Abs(h[0][3]-h[6][3])<1){ stuckFlag.Add(p.Slot); res.Stuck++; res.StuckInfo+=$" {p.C.Name}@({(int)p.Cx/20},{(int)p.Cy/20})t{(int)s.RoundT}"; }
          }
        }
      }
    }
    res.T=s.RoundT; return res;
  }

  public static bool Trace;
  static int Main(string[] a){
    Console.OutputEncoding=Encoding.UTF8;
    if(a.Length>0&&a[0]=="export"){ // 给预览图用：每张大地图一行 JSON
      var o=new StringBuilder();
      for(int i=Maps.StandardCount;i<Maps.All.Length;i++){ var m=Maps.All[i]; var b=new byte[m.Cols*m.Rows]; Maps.Build(b,i);
        o.Append("{\"name\":\""+m.Name+"\",\"cols\":"+m.Cols+",\"rows\":"+m.Rows+",\"hazard\":\""+m.Hazard+"\",\"tiles\":\""+string.Concat(b.Select(x=>(char)('0'+x)))+"\",\"spawn\":["+string.Join(",",m.Spawn.Select(q=>"["+q[0]+","+q[1]+"]"))+"],\"portals\":["+(m.Portals==null?"":string.Join(",",m.Portals.Select(q=>"["+string.Join(",",q)+"]")))+"],");
        o.Append("\"pal\":["+string.Join(",",new uint[]{m.SkyTop,m.SkyBottom,m.Dirt,m.DirtDark,m.Grass,m.GrassLight,m.Stone,m.StoneLight,m.Ice})+"],\"lowgrav\":["+m.LowGravX0+","+m.LowGravX1+"]}\n"); }
      System.IO.File.WriteAllText("maps.jsonl",o.ToString()); return 0; }
    if(a.Length>0&&a[0]=="trace"){ Trace=true; int mi=Array.FindIndex(Maps.All,Maps.StandardCount,m=>m.Name.Contains(a[1])); var r=Round(mi,int.Parse(a[2]),int.Parse(a[3]),a.Length>4&&a[4]=="team"?GameMode.Team:GameMode.Classic); Console.WriteLine($"ended={r.Ended} t={r.T:F0}"); return 0; }
    double k=a.Length>0?double.Parse(a[0]):1; string only=a.Length>1?a[1]:null;
    int fails=0; var sb=new StringBuilder(); var asc=new StringBuilder();
    for(int i=Maps.StandardCount;i<Maps.All.Length;i++){
      var m=Maps.All[i]; if(only!=null && !m.Name.Contains(only)) continue;
      asc.AppendLine(Ascii(i));
      var bad=CheckSpawns(i); foreach(var x in bad) { Console.WriteLine($"[{m.Name}] {x}"); fails++; }
      foreach(int n in new[]{6,10}) foreach(var mode in m.TeamOnly? new[]{GameMode.Team} : new[]{GameMode.Classic, GameMode.Team}){
        int N=Math.Max(4,(int)(24*k)); var rs=new R[N];
        Parallel.For(0,N,j=> rs[j]=Round(i,n,9000+j*31+n*7+i*1000,mode));
        int ended=rs.Count(r=>r.Ended), stuck=rs.Sum(r=>r.Stuck); var ts=rs.Where(r=>r.Ended).Select(r=>r.T).DefaultIfEmpty(0).ToArray();
        string line=$"| {m.Name} | {n} 人 | {(mode==GameMode.Team?"团队":"乱斗")} | {N} | {ended}/{N} | {ts.Average():F0}s | {ts.Max():F0}s | {stuck} |";
        Console.WriteLine(line); sb.AppendLine(line);
        for(int j=0;j<N;j++){ var r=rs[j]; if(!r.Ended || r.Stuck>0) Console.WriteLine($"     seed {9000+j*31+n*7+i*1000} {(r.Ended?"":"未结束 ")}卡住:{r.StuckInfo}"); }
        if(ended<N) fails++;
      }
    }
    System.IO.File.WriteAllText("preview.txt",asc.ToString()); System.IO.File.WriteAllText("results.md",sb.ToString());
    Console.WriteLine(fails==0?"全部通过":$"{fails} 项不通过"); return fails==0?0:1;
  }
}
